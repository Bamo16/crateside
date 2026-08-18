using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works down the stems waiting to be handed to the human half, one track at a time.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No pause.</strong> A file move contends for nothing — not the GPU, not quota — which
/// is the same reason the evidence stage has none.
/// </para>
/// <para>
/// <strong>Its own stage rather than a step at the end of separation</strong>, and the reason is
/// the GPU. Staging sits on an external drive that Ableton may hold files open on, so a move can
/// genuinely fail; folded into separation, a failed move would mark the separation failed, and
/// the retry would spend another six minutes of GPU redoing work that had already succeeded.
/// Separately, only the move is retried.
/// </para>
/// </remarks>
public sealed class HandoffDrainer(
    HandoffRunner runner,
    HandoffSelector selector,
    JobStore jobs,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Handoff;

    /// <summary>Hands off the next track that has stems waiting, if there is one.</summary>
    public Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!selector.TryPickNext(out var videoId, out var stems))
            return Task.FromResult(StageTick.Idle);

        // The ledger is read for a name to log, and for nothing else -- the decision was already
        // made from what is on disk. A track with no line still gets handed off; its stems are
        // real whatever the ledger has to say.
        var subject = KeyLedger.Read(_paths.Ledger).TryGetLatestByVideoId(videoId, out var run)
            ? run.Song
            : videoId;

        jobs.Record(videoId, JobStage.Handoff, JobStatus.Running);

        var result = runner.Run(stems);

        jobs.Record(
            videoId,
            JobStage.Handoff,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error
        );

        return Task.FromResult(StageTick.Ran(subject, result.IsSuccess, result.Error));
    }
}
