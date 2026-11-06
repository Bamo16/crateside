using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Plans;
using Crateside.Core.Tracks;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// The judgments from these bars the user has started, each the track's own state until it is
/// recorded, so it carries on when the page is left (#116).
/// </summary>
public sealed class BarJudging(
    VerdictDrainer drainer,
    WorkingSet workingSet,
    DownloadRunner downloads,
    TimeProvider time
) : IDisposable
{
    private readonly Dictionary<string, BarRun> _runs = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();

    public event Action? Changed;

    /// <summary>The track's latest run, going or finished, since the app started.</summary>
    public BarRun? Of(string trackKey)
    {
        lock (_gate)
            return _runs.GetValueOrDefault(trackKey);
    }

    /// <summary>The order a run from bars judges under: the upload the track is cut from, as the queue would.</summary>
    public static WorkOrder OrderFor(TrackRow row) =>
        new()
        {
            TrackKey = row.TrackKey,
            Title = row.Title,
            SourceUrl = row switch
            {
                { StatedSourceUrl: { Length: > 0 } stated } => stated,
                { TrackKey: var key } when !TrackKeys.IsVideoId(key) => TrackKeys.ProvenanceOf(key),
                _ => $"https://music.youtube.com/watch?v={row.TrackKey}",
            },
            Destination = row.Verdict?.HasTaggableKey is true
                ? StemDestination.KeyIdentified
                : StemDestination.UnresolvedKey,
        };

    /// <summary>The full mix to detect over: the working set's, else the source kept in the library; null when it has to be downloaded again.</summary>
    public string? AudioFor(TrackRow row) =>
        workingSet.TryGetSource(row.TrackKey, out var held) ? held
        : row.Samples.FirstOrDefault(sample =>
            sample.Kind is StemKind.Source && File.Exists(sample.Path)
        )
            is { } kept
            ? kept.Path
        : null;

    /// <summary>Starts a run in the background; false when one for this track is already going.</summary>
    public bool TryStart(WorkOrder order, JudgeDirection direction)
    {
        lock (_gate)
        {
            if (_runs.GetValueOrDefault(order.TrackKey) is { IsRunning: true })
                return false;

            _runs[order.TrackKey] = new BarRun(direction, time.GetUtcNow());
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
        var madeForTheRun = !workingSet.TrackKeys.Contains(order.TrackKey, StringComparer.Ordinal);
        VerdictResult result;

        try
        {
            result = await JudgeAsync(order, direction);
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
            workingSet.Discard(order.TrackKey);

        Update(order.TrackKey, run => run with { Outcome = result, FinishedAt = time.GetUtcNow() });
    }

    /// <summary>Downloads the full mix again where crateside no longer holds it, then judges.</summary>
    private async Task<VerdictResult> JudgeAsync(WorkOrder order, JudgeDirection direction)
    {
        if (direction.Audio is null)
        {
            Update(order.TrackKey, run => run.After(new JudgeStep.Downloading(), time.GetUtcNow()));

            var fetched = await downloads.RunAsync(
                order.SourceUrl,
                workingSet.Prepare(order.TrackKey),
                ct: _stopping.Token
            );

            if (fetched is not { IsSuccess: true, Path: { } path })
                return VerdictResult.Failed(
                    $"the full mix could not be downloaded again: {fetched.Error}"
                );

            direction = direction with { Audio = path };
        }

        return await drainer.JudgeFromBarsAsync(
            order,
            direction,
            step => Update(order.TrackKey, run => run.After(step, time.GetUtcNow())),
            _stopping.Token
        );
    }

    private void Update(string trackKey, Func<BarRun, BarRun> change)
    {
        lock (_gate)
            _runs[trackKey] = change(_runs[trackKey]);

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
