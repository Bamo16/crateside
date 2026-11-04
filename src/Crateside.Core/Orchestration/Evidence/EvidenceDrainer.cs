using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Evidence;

/// <summary>
/// Works the queue down one detection at a time, for as long as anything is undetected.
/// </summary>
public sealed class EvidenceDrainer(
    LedgerStore store,
    WorkListService queue,
    EvidenceRunner runner,
    EvidenceSelector selector,
    DetectorBackfill backfill,
    JobStore jobs,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Evidence;

    /// <summary>
    /// Detects the next undetected track; with none queued, fills one judged song's missing readings
    /// instead (#139).
    /// </summary>
    public async Task<StageTick> DrainOnceAsync(CancellationToken ct = default)
    {
        WorkListsView view;

        try
        {
            view = await queue.ReadAsync(ct);
        }
        catch (PlaylistUnavailableException ex)
        {
            return StageTick.Unavailable(ex.Message);
        }

        return await DrainAsync(view, ct);
    }

    internal async Task<StageTick> DrainAsync(WorkListsView view, CancellationToken ct)
    {
        if (!selector.TryPickNext(view, out var item))
            return await BackfillOnceAsync(ct);

        var order = WorkOrder.Compose(item, store.Read());

        // Recorded before the run starts, so a crash leaves the job "Running" rather than
        // absent, distinguishing an interrupted attempt from one never made.
        jobs.Record(item.TrackKey, JobStage.Evidence, JobStatus.Running);

        var result = await runner.RunAsync(order, ct);

        jobs.Record(
            item.TrackKey,
            JobStage.Evidence,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error,
            summary: result.Summary
        );

        return StageTick.Ran(order, result.IsSuccess, result.Error);
    }

    private async Task<StageTick> BackfillOnceAsync(CancellationToken ct)
    {
        if (await backfill.NextAsync(ct) is not { } target)
            return StageTick.Idle;

        var result = await backfill.ReadAsync(target, ct);
        var verb = target.Rereads ? "re-read on keydetect's pinned build" : "added";

        return StageTick.Ran(
            result.Song,
            result.Failed.Count is 0,
            result.Failed.Count is 0
                ? null
                : string.Join(
                    "; ",
                    result.Failed.Select(failed => $"{failed.Key}: {failed.Value}")
                ),
            result.Added.Count is 0 ? null : $"{verb} {string.Join(", ", result.Added)}",
            wakesOthers: false
        );
    }
}
