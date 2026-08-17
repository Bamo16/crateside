using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works the queue down one detection at a time, for as long as anything is undetected.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No pause.</strong> Evidence contends for nothing — no GPU, no quota — which is the
/// premise the whole three-stage split rests on, so there is nothing to stop it for and nothing
/// to pick a moment for.
/// </para>
/// <para>
/// A loop of its own rather than a step inside one: a single sequential loop would mean a paused
/// GPU stopped detection of everything queued behind it, and a stage failing on one track would
/// stall other tracks in other stages.
/// </para>
/// </remarks>
public sealed class EvidenceDrainer(
    QueueService queue,
    EvidenceRunner runner,
    EvidenceSelector selector,
    JobStore jobs,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Evidence;

    /// <summary>Detects the next undetected track, if there is one.</summary>
    public async Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        QueueView view;

        try
        {
            view = await queue.ReadAsync(cancellationToken);
        }
        catch (QueueUnavailableException e)
        {
            // The playlist is unreadable — a lapsed credential, or it went private. Something to
            // report and retry, never a reason to stop draining for good.
            return StageTick.Unavailable(e.Message);
        }

        if (!selector.TryPickNext(view, out var item))
            return StageTick.Idle;

        var order = WorkOrder.Compose(item, KeyLedger.Read(_paths.Ledger));

        // Recorded before the work starts, so a crash mid-run leaves a job that reads "Running"
        // rather than nothing at all — which is what the next pass needs in order to tell an
        // interrupted attempt from one that was never made.
        jobs.Record(item.VideoId, JobStage.Evidence, JobStatus.Running);

        var result = await runner.RunAsync(order, cancellationToken);

        jobs.Record(
            item.VideoId,
            JobStage.Evidence,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error
        );

        return StageTick.Ran(order, result.IsSuccess, result.Error);
    }
}
