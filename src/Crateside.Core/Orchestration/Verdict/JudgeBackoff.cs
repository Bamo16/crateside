namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// How long judging holds after the endpoint refused a session: the wait it stated, else a minute
/// doubling to thirty, until a session is served (ADR 0070).
/// </summary>
public sealed class JudgeBackoff(TimeProvider time)
{
    private static readonly TimeSpan First = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(30);

    private readonly Lock _gate = new();
    private int _refusals;
    private JudgeHold? _hold;

    /// <summary>The hold in force, or null once its time has passed.</summary>
    public JudgeHold? Hold
    {
        get
        {
            lock (_gate)
                return _hold is { RetryAt: { } until } && until > time.GetUtcNow() ? _hold : null;
        }
    }

    /// <summary>Holds judging after a refusal, for <paramref name="retryAfter"/> where the endpoint stated one.</summary>
    public JudgeHold Refused(string reason, TimeSpan? retryAfter)
    {
        lock (_gate)
        {
            _refusals++;

            return _hold = new JudgeHold(
                reason,
                time.GetUtcNow() + (retryAfter ?? Wait(_refusals))
            );
        }
    }

    /// <summary>Clears the hold and starts the next refusal's wait at a minute again.</summary>
    public void Served()
    {
        lock (_gate)
        {
            _refusals = 0;
            _hold = null;
        }
    }

    /// <summary>The wait after the <paramref name="refusals"/>th refusal in a row that stated none.</summary>
    public static TimeSpan Wait(int refusals) =>
        refusals > 5 ? Longest : First * (1 << (refusals - 1));
}
