using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// How long judging holds after the endpoint refused a session: the wait it stated, else a minute
/// doubling to thirty, until a session is served (ADR 0070).
/// </summary>
public sealed class JudgeBackoff
{
    /// <summary>Refusals in a row, with no session served between, after which judging pauses itself.</summary>
    public const int PauseAfter = 5;

    private static readonly TimeSpan First = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _time;
    private readonly PauseStore? _remembered;
    private readonly Lock _gate = new();
    private int _refusals;
    private JudgeHold? _hold;

    /// <summary>Takes up the refusals and hold <paramref name="remembered"/> kept, so a restart does not forget them.</summary>
    public JudgeBackoff(TimeProvider time, PauseStore? remembered = null)
    {
        _time = time;
        _remembered = remembered;

        if (remembered?.Read().Backoff is { } kept)
            (_refusals, _hold) = (kept.Refusals, kept.Hold);
    }

    /// <summary>Refusals since a session was last served.</summary>
    public int Refusals
    {
        get
        {
            lock (_gate)
                return _refusals;
        }
    }

    /// <summary>Whether enough refusals came in a row that judging stays paused until the user resumes it.</summary>
    public bool IsExhausted => Refusals >= PauseAfter;

    /// <summary>The latest refusal, whether or not its wait has passed.</summary>
    public JudgeHold? Last
    {
        get
        {
            lock (_gate)
                return _hold;
        }
    }

    /// <summary>The hold in force, or null once its time has passed.</summary>
    public JudgeHold? Hold
    {
        get
        {
            lock (_gate)
                return _hold is { RetryAt: { } until } && until > _time.GetUtcNow() ? _hold : null;
        }
    }

    /// <summary>Holds judging after a refusal, for <paramref name="retryAfter"/> where the endpoint stated one.</summary>
    public JudgeHold Refused(string reason, TimeSpan? retryAfter)
    {
        lock (_gate)
        {
            _refusals++;
            _hold = new JudgeHold(reason, _time.GetUtcNow() + (retryAfter ?? Wait(_refusals)));
            _remembered?.RememberBackoff(new JudgeBackoffState(_refusals, _hold));

            return _hold;
        }
    }

    /// <summary>Clears the hold and starts the next refusal's wait at a minute again.</summary>
    public void Served() => Clear();

    /// <summary>Forgets the refusals and the hold, as the user's resume does, so judging tries at once.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _refusals = 0;
            _hold = null;
            _remembered?.RememberBackoff(null);
        }
    }

    /// <summary>The wait after the <paramref name="refusals"/>th refusal in a row that stated none.</summary>
    public static TimeSpan Wait(int refusals) =>
        refusals > 5 ? Longest : First * (1 << (refusals - 1));
}

/// <summary>What <see cref="JudgeBackoff"/> keeps across a restart.</summary>
public sealed record JudgeBackoffState(int Refusals, JudgeHold? Hold);
