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
    JobStore jobs,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Evidence;

    /// <summary>Detects the next undetected track, if there is one.</summary>
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

        if (!selector.TryPickNext(view, out var item))
            return StageTick.Idle;

        var order = WorkOrder.Compose(item, store.Read());

        // Recorded before the run starts, so a crash leaves the job "Running" rather than
        // absent, distinguishing an interrupted attempt from one never made.
        jobs.Record(item.VideoId, JobStage.Evidence, JobStatus.Running);

        var result = await runner.RunAsync(order, ct);

        jobs.Record(
            item.VideoId,
            JobStage.Evidence,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error
        );

        return StageTick.Ran(order, result.IsSuccess, result.Error);
    }
}
