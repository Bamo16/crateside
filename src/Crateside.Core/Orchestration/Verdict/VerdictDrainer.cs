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
    LedgerStore store,
    WorkListService queue,
    VerdictRunner runner,
    VerdictSelector selector,
    JobStore jobs,
    VerdictControl control,
    QuotaGuard guard,
    RejudgeQueue rejudges,
    WorkingSet workingSet,
    IOptions<CratesidePaths> paths
) : IStageDrainer, IBackfillJudge
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
    /// Judges one track for the weekly spend-down, paused or not: new intake, then a song owed a
    /// re-judge, then a library backfill track. The spend-down's ceiling replaces the weekly hold (ADR 0063).
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

    /// <summary>
    /// Judges one track from the windows the user gave, paused or held or not, once any judgment
    /// in progress has finished: the user asked for this one (#116).
    /// </summary>
    public async Task<VerdictResult> JudgeFromBarsAsync(
        WorkOrder order,
        JudgeDirection direction,
        Action<JudgeStep> onStep,
        CancellationToken ct = default
    )
    {
        using (control.WaitingFormRun())
            await _oneAtATime.WaitAsync(ct);

        try
        {
            jobs.Record(order.VideoId, JobStage.Verdict, JobStatus.Running);
            control.Report($"Judging {order.Title} from bars");

            var result = await runner.RunAsync(order, direction, onStep, ct);

            jobs.Record(
                order.VideoId,
                JobStage.Verdict,
                result switch
                {
                    { IsSuccess: true } => JobStatus.Succeeded,
                    { IsAgentUnavailable: true } => JobStatus.Cancelled,
                    _ => JobStatus.Failed,
                },
                error: result.Error
            );

            if (result.IsAgentUnavailable)
                control.Pause();

            return result;
        }
        finally
        {
            control.Report(null);
            _oneAtATime.Release();
        }
    }

    /// <summary>The songs owed a re-judge whose evidence the working set holds or the ledger can give back.</summary>
    private IReadOnlyList<LedgerRun> WithEvidence(IReadOnlyList<LedgerRun> owed)
    {
        if (owed.All(HasWorkingEvidence))
            return owed;

        var held = store.LatestEvidenceJson();

        return [.. owed.Where(run => HasWorkingEvidence(run) || held.ContainsKey(run.Song))];
    }

    private bool HasWorkingEvidence(LedgerRun run) =>
        run is { SourceVideoId: { } videoId } && workingSet.HasEvidence(videoId);

    /// <summary>
    /// Puts back the evidence the janitor cleared and drops the cached source pages, so the new
    /// session reads them afresh; the run keeps the upload the last one was judged from.
    /// </summary>
    private WorkOrder PrepareRejudge(WorkOrder order, KeyLedger ledger)
    {
        var folder = workingSet.Prepare(order.VideoId);

        if (
            !workingSet.HasEvidence(order.VideoId)
            && ledger.SongFor(order.VideoId) is { } song
            && store.LatestEvidenceJson().TryGetValue(song, out var evidence)
        )
            File.WriteAllText(workingSet.EvidencePathFor(order.VideoId), evidence);

        File.Delete(Path.Combine(folder, VerdictRunner.SourcesFileName));

        return ledger.TryGetLatestByVideoId(order.VideoId, out var run) && run.SourceUrl is { } url
            ? order with
            {
                SourceUrl = url,
            }
            : order;
    }

    private async Task<StageTick> JudgeNextAsync(bool backfill, CancellationToken ct)
    {
        var hold = backfill ? null : await guard.CheckAsync(ct);

        if (!backfill && hold is null)
            guard.ClearPass();

        if (hold is not null && guard.PassedVideoId is null)
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
        var ledger = store.Read();

        WorkItem item;
        var isRejudge = false;

        var picked = (backfill, hold, guard.PassedVideoId) switch
        {
            (true, _, _) => selector.TryPickIdle(
                view,
                ledger,
                WithEvidence(rejudges.Pending(ledger)),
                includeIntake: !control.IsPaused,
                out item,
                out isRejudge
            ),
            // Held, so only the track the user let through is judged
            (_, not null, { } passed) => selector.TryPick(view, ledger, passed, out item),
            _ => selector.TryPickNext(view, ledger, out item),
        };

        if (!picked)
            return hold is { } held ? StageTick.Held(held.Reason) : StageTick.Idle;

        if (hold is not null)
            guard.ClearPass();

        var order = WorkOrder.Compose(item, ledger);

        if (isRejudge)
            order = PrepareRejudge(order, ledger);

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
