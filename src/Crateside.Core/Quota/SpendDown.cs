using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Quota;

/// <summary>Judges one backlog track: new intake, then a re-judge, then the library backfill.</summary>
public interface IBackfillJudge
{
    Task<StageTick> JudgeBackfillAsync(CancellationToken ct = default);
}

/// <summary>
/// Judges the backlog in the hours before the weekly reset until weekly use reaches its ceiling,
/// never while the five-hour window is nearly spent (ADR 0063).
/// </summary>
public sealed class SpendDown(
    PauseStore remembered,
    SpendDownStore store,
    IQuotaReader reader,
    IBackfillJudge judge,
    TimeProvider time
)
{
    private static readonly TimeSpan CutOffFallback = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();

    private DateTimeOffset? _cutOffUntil;
    private bool _enabled = remembered.Read().Backfill;
    private SpendDownSettings _settings = store.Read();
    private QuotaSnapshot _last = QuotaSnapshot.Unknown;
    private SpendDownState? _state;

    /// <summary>Raised whenever the state changes, so a rendered page can catch up.</summary>
    public event Action? Changed;

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _enabled;
        }
    }

    public SpendDownSettings Settings
    {
        get
        {
            lock (_gate)
                return _settings;
        }
    }

    /// <summary>Where the spend-down stood at its last check, or null before the first.</summary>
    public SpendDownState? State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public static SpendDownState Decide(
        QuotaSnapshot quota,
        SpendDownSettings chosen,
        DateTimeOffset now,
        DateTimeOffset? cutOffUntil = null
    )
    {
        if (
            quota is not { WeeklyUtilization: { } weekly, WeeklyResetsAt: { } resets }
            || resets <= now
        )
            return new SpendDownState.Unread();

        var opens = resets - TimeSpan.FromHours(chosen.WindowHours);

        if (now < opens)
            return new SpendDownState.Waiting(opens, resets);

        if (weekly >= chosen.WeeklyCeiling)
            return new SpendDownState.Spent(weekly, resets);

        if (cutOffUntil is { } until && now < until)
            return new SpendDownState.CutOff(until);

        if (quota.Utilization is not { } session)
            return new SpendDownState.Unread();

        if (session >= chosen.SessionCeiling)
            return new SpendDownState.SessionFull(session, quota.ResetsAt);

        return new SpendDownState.Open(weekly, resets);
    }

    public void SetEnabled(bool enabled)
    {
        remembered.RememberBackfill(enabled);

        lock (_gate)
            _enabled = enabled;

        Reconsider();
    }

    public void Remember(SpendDownSettings chosen)
    {
        store.Remember(chosen);

        lock (_gate)
            _settings = store.Read();

        Reconsider();
    }

    /// <summary>
    /// Judges one backlog track if the spend-down is open, reading weekly use before and after
    /// it; null when nothing was judged.
    /// </summary>
    public async Task<SpendDownPass?> RunOnceAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            Reconsider();

            return null;
        }

        var before = await ReadAsync(ct);

        if (Settle(before) is not SpendDownState.Open { Weekly: var weeklyBefore })
            return null;

        var tick = await judge.JudgeBackfillAsync(ct);

        if (tick.Outcome is not StageOutcome.Ran)
            return null;

        if (tick.IsAgentUnavailable)
        {
            lock (_gate)
                _cutOffUntil =
                    before.ResetsAt is { } rolls && rolls > time.GetUtcNow()
                        ? rolls
                        : time.GetUtcNow() + CutOffFallback;

            Settle(before);

            return new SpendDownPass(tick, weeklyBefore, WeeklyAfter: null);
        }

        var after = await ReadAsync(ct);

        Settle(after);

        return new SpendDownPass(tick, weeklyBefore, after.WeeklyUtilization);
    }

    private async Task<QuotaSnapshot> ReadAsync(CancellationToken ct)
    {
        var quota = await reader.ReadAsync(ct);

        lock (_gate)
            _last = quota;

        return quota;
    }

    private void Reconsider()
    {
        QuotaSnapshot last;

        lock (_gate)
            last = _last;

        Settle(last);
    }

    private SpendDownState Settle(QuotaSnapshot quota)
    {
        SpendDownState state;

        lock (_gate)
            _state = state = _enabled
                ? Decide(quota, _settings, time.GetUtcNow(), _cutOffUntil)
                : new SpendDownState.Off();

        Changed?.Invoke();

        return state;
    }
}

public abstract record SpendDownState
{
    private SpendDownState() { }

    public sealed record Off : SpendDownState;

    /// <summary>Weekly or five-hour use, or the weekly reset, could not be read.</summary>
    public sealed record Unread : SpendDownState;

    public sealed record Waiting(DateTimeOffset OpensAt, DateTimeOffset ResetsAt) : SpendDownState;

    public sealed record Open(double Weekly, DateTimeOffset ResetsAt) : SpendDownState;

    /// <summary>Weekly use has reached the ceiling, so nothing more is judged before the reset.</summary>
    public sealed record Spent(double Weekly, DateTimeOffset ResetsAt) : SpendDownState;

    /// <summary>A track was cut off by the usage limit, so nothing more starts until the five-hour window rolls.</summary>
    public sealed record CutOff(DateTimeOffset ResumesAt) : SpendDownState;

    public sealed record SessionFull(double Session, DateTimeOffset? ResetsAt) : SpendDownState;
}

/// <param name="WeeklyAfter">Null when the reading after the track failed.</param>
public sealed record SpendDownPass(StageTick Tick, double WeeklyBefore, double? WeeklyAfter)
{
    public double? Delta => WeeklyAfter - WeeklyBefore;
}
