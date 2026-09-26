using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

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
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public JobStage Stage => JobStage.Verdict;

    /// <summary>Judges the next detected-but-unjudged track, if there is one.</summary>
    public async Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        await _oneAtATime.WaitAsync(cancellationToken);

        try
        {
            return control.IsPaused
                ? StageTick.Paused
                : await JudgeNextAsync(backfill: false, cancellationToken);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Judges one library backfill track, paused or not: what an idle window spends (ADR 0035).
    /// </summary>
    public async Task<StageTick> JudgeBackfillAsync(CancellationToken cancellationToken = default)
    {
        await _oneAtATime.WaitAsync(cancellationToken);

        try
        {
            return await JudgeNextAsync(backfill: true, cancellationToken);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<StageTick> JudgeNextAsync(bool backfill, CancellationToken cancellationToken)
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

        // Read once and used for both the choice and the order, so the two cannot disagree.
        var ledger = KeyLedger.Read(_paths.Ledger);

        var picked = backfill
            ? selector.TryPickBackfill(view, ledger, out var item)
            : selector.TryPickNext(view, ledger, out item);

        if (!picked)
            return StageTick.Idle;

        var order = WorkOrder.Compose(item, ledger);

        jobs.Record(item.VideoId, JobStage.Verdict, JobStatus.Running);
        control.Report($"Judging {order.Title}");

        try
        {
            var result = await runner.RunAsync(order, cancellationToken);

            // Cancelled, not Failed: a session that never ran is not this track's fault, and
            // Cancelled carries no strike. Then paused, since a further attempt would return
            // instantly and spend a strike for nothing — on every track in the queue, not just
            // this one.
            if (result.IsAgentUnavailable)
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
