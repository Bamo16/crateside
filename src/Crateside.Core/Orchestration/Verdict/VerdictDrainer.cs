using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.Quota;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// Works the queue down one judgment at a time, for as long as anything detected is unjudged.
/// </summary>
public sealed class VerdictDrainer(
    WorkListService queue,
    VerdictRunner runner,
    VerdictSelector selector,
    JobStore jobs,
    VerdictControl control,
    QuotaGuard guard,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public JobStage Stage => JobStage.Verdict;

    /// <summary>Judges the next detected-but-unjudged track, if there is one.</summary>
    public async Task<StageTick> DrainOnceAsync(CancellationToken ct = default)
    {
        await _oneAtATime.WaitAsync(ct);

        try
        {
            return control.IsPaused ? StageTick.Paused : await JudgeNextAsync(backfill: false, ct);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Judges one library backfill track, paused or not: what an idle window spends (ADR 0035).
    /// </summary>
    public async Task<StageTick> JudgeBackfillAsync(CancellationToken ct = default)
    {
        await _oneAtATime.WaitAsync(ct);

        try
        {
            return await JudgeNextAsync(backfill: true, ct);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<StageTick> JudgeNextAsync(bool backfill, CancellationToken ct)
    {
        var hold = await guard.CheckAsync(ct);

        if (hold is null)
            guard.ClearPass();

        if (hold is not null && (backfill || guard.PassedVideoId is null))
            return StageTick.Held(hold.Reason);

        WorkListsView view;

        try
        {
            view = await queue.ReadAsync(ct);
        }
        catch (PlaylistUnavailableException ex)
        {
            return StageTick.Unavailable(ex.Message);
        }

        // Read once and used for both the choice and the order, so the two cannot disagree.
        var ledger = KeyLedger.Read(_paths.Ledger);

        WorkItem item;

        var picked = (backfill, hold, guard.PassedVideoId) switch
        {
            (true, _, _) => selector.TryPickBackfill(view, ledger, out item),
            // Held, so only the track the user let through is judged
            (_, not null, { } passed) => selector.TryPick(view, ledger, passed, out item),
            _ => selector.TryPickNext(view, ledger, out item),
        };

        if (!picked)
            return hold is { } held ? StageTick.Held(held.Reason) : StageTick.Idle;

        if (hold is not null)
            guard.ClearPass();

        var order = WorkOrder.Compose(item, ledger);

        jobs.Record(item.VideoId, JobStage.Verdict, JobStatus.Running);
        control.Report($"Judging {order.Title}");

        try
        {
            var result = await runner.RunAsync(order, ct);

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
