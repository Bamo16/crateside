using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// The judgments from these bars the user has started, each the track's own state until it is
/// recorded, so it carries on when the page is left (#116).
/// </summary>
public sealed class BarJudging(VerdictDrainer drainer, WorkingSet workingSet, TimeProvider time)
    : IDisposable
{
    private readonly Dictionary<string, BarRun> _runs = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();

    public event Action? Changed;

    /// <summary>The track's latest run, going or finished, since the app started.</summary>
    public BarRun? Of(string videoId)
    {
        lock (_gate)
            return _runs.GetValueOrDefault(videoId);
    }

    /// <summary>Starts a run in the background; false when one for this track is already going.</summary>
    public bool TryStart(WorkOrder order, JudgeDirection direction)
    {
        lock (_gate)
        {
            if (_runs.GetValueOrDefault(order.VideoId) is { IsRunning: true })
                return false;

            _runs[order.VideoId] = new BarRun(direction, time.GetUtcNow());
        }

        Changed?.Invoke();
        _ = Task.Run(() => RunAsync(order, direction));

        return true;
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task RunAsync(WorkOrder order, JudgeDirection direction)
    {
        var madeForTheRun = !workingSet.VideoIds.Contains(order.VideoId, StringComparer.Ordinal);
        VerdictResult result;

        try
        {
            result = await drainer.JudgeFromBarsAsync(
                order,
                direction,
                step => Update(order.VideoId, run => run.After(step, time.GetUtcNow())),
                _stopping.Token
            );
        }
        catch (OperationCanceledException)
        {
            result = VerdictResult.Failed("crateside stopped before the run finished");
        }
        // Nothing awaits this task, so whatever escapes would leave the run showing as going.
        catch (Exception ex)
        {
            result = VerdictResult.Failed(ex.Message);
        }

        if (madeForTheRun)
            workingSet.Discard(order.VideoId);

        Update(order.VideoId, run => run with { Outcome = result, FinishedAt = time.GetUtcNow() });
    }

    private void Update(string videoId, Func<BarRun, BarRun> change)
    {
        lock (_gate)
            _runs[videoId] = change(_runs[videoId]);

        Changed?.Invoke();
    }
}

/// <summary>One judgment from these bars, as far as it has got.</summary>
public sealed record BarRun(JudgeDirection Direction, DateTimeOffset StartedAt)
{
    public JudgeStep? Step { get; init; }

    /// <summary>The windows the judge asked for during the run, and when.</summary>
    public IReadOnlyList<RunWindow> JudgeAsked { get; init; } = [];

    public DateTimeOffset? JudgeAskedAt { get; init; }

    /// <summary>Whether the judge ended once without a verdict and was asked again.</summary>
    public bool Resumed { get; init; }

    /// <summary>Null while the run is going.</summary>
    public VerdictResult? Outcome { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsRunning => Outcome is null;

    /// <summary>Every window of the run, the user's first.</summary>
    public IReadOnlyList<RunWindow> Windows => [.. Direction.Windows, .. JudgeAsked];

    internal BarRun After(JudgeStep step, DateTimeOffset now) =>
        step switch
        {
            JudgeStep.JudgeAsked(var asked) => this with
            {
                Step = step,
                JudgeAsked = asked,
                JudgeAskedAt = now,
            },
            JudgeStep.Resuming => this with { Step = step, Resumed = true },
            _ => this with { Step = step },
        };
}
