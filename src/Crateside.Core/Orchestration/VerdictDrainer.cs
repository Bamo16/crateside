using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works the queue down one judgment at a time, for as long as anything detected is unjudged.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No pause.</strong> Claude quota is genuinely tight during working sessions, but a
/// verdict run is now cheap — evidence in, verdict out, no download and no separation — so it
/// does not earn a control. ADR 0001's reasoning stands; the consequence it implied does not,
/// and this note exists so nobody re-derives one from it.
/// </para>
/// <para>
/// A loop of its own so that a track waiting on the GPU never holds up one that is ready to be
/// judged, and vice versa.
/// </para>
/// </remarks>
public sealed class VerdictDrainer(
    QueueService queue,
    VerdictRunner runner,
    VerdictSelector selector,
    JobStore jobs,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Verdict;

    /// <summary>Judges the next detected-but-unjudged track, if there is one.</summary>
    public async Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        QueueView view;

        try
        {
            view = await queue.ReadAsync(cancellationToken);
        }
        catch (QueueUnavailableException e)
        {
            return StageTick.Unavailable(e.Message);
        }

        // Read once and used for both the choice and the order, so the two cannot disagree about
        // what the ledger said.
        var ledger = KeyLedger.Read(_paths.Ledger);

        if (!selector.TryPickNext(view, ledger, out var item))
            return StageTick.Idle;

        var order = WorkOrder.Compose(item, ledger);

        jobs.Record(item.VideoId, JobStage.Verdict, JobStatus.Running);

        var result = await runner.RunAsync(order, cancellationToken);

        jobs.Record(
            item.VideoId,
            JobStage.Verdict,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error
        );

        return StageTick.Ran(order, result.IsSuccess, result.Error);
    }
}
