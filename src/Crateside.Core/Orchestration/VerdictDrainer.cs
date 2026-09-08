using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works the queue down one judgment at a time, for as long as anything detected is unjudged.
/// </summary>
public sealed class VerdictDrainer(
    QueueService queue,
    VerdictRunner runner,
    VerdictSelector selector,
    JobStore jobs,
    VerdictControl control,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Verdict;

    /// <summary>Judges the next detected-but-unjudged track, if there is one.</summary>
    public async Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        if (control.IsPaused)
            return StageTick.Paused;

        QueueView view;

        try
        {
            view = await queue.ReadAsync(cancellationToken);
        }
        catch (QueueUnavailableException e)
        {
            return StageTick.Unavailable(e.Message);
        }

        // Read once and used for both the choice and the order, so the two cannot disagree.
        var ledger = KeyLedger.Read(_paths.Ledger);

        if (!selector.TryPickNext(view, ledger, out var item))
            return StageTick.Idle;

        var order = WorkOrder.Compose(item, ledger);

        jobs.Record(item.VideoId, JobStage.Verdict, JobStatus.Running);
        control.Report($"Judging {order.Title}");

        try
        {
            var result = await runner.RunAsync(order, cancellationToken);

            // Cancelled, not Failed: running out of allowance is not this track's fault, and
            // Cancelled carries no strike. Then paused, since a further attempt would return
            // instantly and spend a strike for nothing.
            if (result.IsQuotaExhausted)
            {
                jobs.Record(
                    item.VideoId,
                    JobStage.Verdict,
                    JobStatus.Cancelled,
                    error: result.Error
                );
                control.Pause();

                return StageTick.Ran(order, isSuccess: false, result.Error);
            }

            jobs.Record(
                item.VideoId,
                JobStage.Verdict,
                result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
                error: result.Error
            );

            return StageTick.Ran(order, result.IsSuccess, result.Error);
        }
        finally
        {
            control.Report(null);
        }
    }
}
