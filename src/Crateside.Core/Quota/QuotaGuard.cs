namespace Crateside.Core.Quota;

/// <summary>
/// Holds judging back while the weekly Claude limit is nearly spent, and lets go by itself once
/// the reading drops or the reset passes. Not the user's pause, which persists (ADR 0053).
/// </summary>
public sealed class QuotaGuard(Func<CancellationToken, Task<QuotaSnapshot>> read, TimeProvider time)
{
    /// <summary>Weekly use, as a percentage, at which judging stops.</summary>
    public const double WeeklyLimit = 85;

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly Lock _gate = new();

    private QuotaSnapshot _last = QuotaSnapshot.Unknown;
    private DateTimeOffset _readAt;
    private string? _passedVideoId;

    /// <summary>The one track the user let through the hold, held in memory only.</summary>
    public string? PassedVideoId
    {
        get
        {
            lock (_gate)
                return _passedVideoId;
        }
    }

    /// <summary>
    /// The hold as of the last reading, without reading again. Before any reading has succeeded
    /// the hold is on, since an unread quota is not a spare one (ADR 0053).
    /// </summary>
    public QuotaHold? Hold => _last.IsKnown ? HoldOf(_last, time.GetUtcNow()) : QuotaHold.Unread;

    /// <summary>The hold a reading puts on judging at <paramref name="now"/>, or null if none.</summary>
    public static QuotaHold? HoldOf(QuotaSnapshot quota, DateTimeOffset now) =>
        quota is { WeeklyUtilization: >= WeeklyLimit and var used }
        && (quota.WeeklyResetsAt is not { } resets || now < resets)
            ? new QuotaHold(used, quota.WeeklyResetsAt)
            : null;

    /// <summary>Lets this one track be judged despite the hold; a second call replaces the first.</summary>
    public void Pass(string videoId)
    {
        lock (_gate)
            _passedVideoId = videoId;
    }

    public void ClearPass()
    {
        lock (_gate)
            _passedVideoId = null;
    }

    /// <summary>
    /// The hold now, reading the quota again if the last reading is old. A reading that fails
    /// leaves the previous one standing.
    /// </summary>
    public async Task<QuotaHold?> CheckAsync(CancellationToken ct = default)
    {
        await _reading.WaitAsync(ct);

        try
        {
            if (time.GetUtcNow() - _readAt >= MaxAge)
            {
                var quota = await read(ct);

                if (quota.IsKnown)
                    _last = quota;

                _readAt = time.GetUtcNow();
            }
        }
        finally
        {
            _reading.Release();
        }

        return Hold;
    }
}

/// <param name="Used">Weekly use when read, as a percentage, or null if it has not been read.</param>
/// <param name="ResumesAt">When the weekly limit resets, or null if the payload did not say.</param>
public sealed record QuotaHold(double? Used, DateTimeOffset? ResumesAt)
{
    public static QuotaHold Unread { get; } = new(null, null);

    public string Reason =>
        Used is not { } used
            ? "Weekly Claude use has not been read yet, so judging waits for a reading."
            : $"Weekly Claude use is at {used:N0}%, past the {QuotaGuard.WeeklyLimit:N0}% limit. "
                + (
                    ResumesAt is { } at
                        ? $"The hold lifts when it resets at {at.ToLocalTime():ddd h:mm tt}."
                        : "The hold lifts once it drops."
                );
}
