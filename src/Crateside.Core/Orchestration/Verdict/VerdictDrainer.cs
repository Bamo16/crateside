using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Evidence;
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
    EvidenceCompleter completer,
    PauseStore remembered,
    IOptions<CratesidePaths> paths,
    IOptions<JudgeOptions> judge,
    TimeProvider time
) : IStageDrainer, IBackfillJudge
{
    private readonly CratesidePaths _paths = paths.Value;
    private readonly bool _spendsClaudeQuota = judge.Value.SpendsClaudeQuota;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public JobStage Stage => JobStage.Verdict;

    /// <summary>Judges the next detected-but-unjudged track, if there is one.</summary>
    public async Task<StageTick> DrainOnceAsync(CancellationToken ct = default)
    {
        await _oneAtATime.WaitAsync(ct);

        try
        {
            if (control.IsPaused)
            {
                control.SetHold(null);

                return StageTick.Paused;
            }

            var tick = await JudgeNextAsync(backfill: false, ct);

            control.SetHold(
                tick is { Outcome: StageOutcome.Held, Error: { } reason }
                    ? new JudgeHold(
                        reason,
                        tick.RetryAfter is { } wait ? time.GetUtcNow() + wait : null
                    )
                    : null
            );

            return tick;
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

            var result =
                workingSet.HasEvidence(order.VideoId)
                && await completer.CompleteAsync(order.VideoId, direction.Audio, ct)
                    is { } incomplete
                    ? VerdictResult.Failed($"not judged: {incomplete}")
                    : await runner.RunAsync(order, direction, onStep, ct);

            jobs.Record(
                order.VideoId,
                JobStage.Verdict,
                result switch
                {
                    { IsSuccess: true } => JobStatus.Succeeded,
                    { IsAgentUnavailable: true } => JobStatus.Canceled,
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

    /// <summary>How many songs are owed a re-judge that the drainer can actually run.</summary>
    public int RejudgesWithEvidence(KeyLedger ledger) =>
        WithEvidence(rejudges.Pending(ledger)).Count;

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

        return await JudgeNextAsync(view, backfill, hold, ct);
    }

    internal async Task<StageTick> JudgeNextAsync(
        WorkListsView view,
        bool backfill,
        QuotaHold? hold,
        CancellationToken ct
    )
    {
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
            // A judge off the weekly Claude limit works the backlog whenever backfill is on (ADR 0067)
            _ when !_spendsClaudeQuota && remembered.Read().Backfill => selector.TryPickIdle(
                view,
                ledger,
                WithEvidence(rejudges.Pending(ledger)),
                includeIntake: true,
                out item,
                out isRejudge
            ),
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
            if (await completer.CompleteAsync(item.VideoId, ct: ct) is { } incomplete)
            {
                var error = $"not judged: {incomplete}";

                jobs.Record(item.VideoId, JobStage.Verdict, JobStatus.Failed, error: error);

                return StageTick.Ran(order, false, error);
            }

            var result = await runner.RunAsync(order, ct);

            // Canceled, not Failed: a session that never ran is not this track's fault, and
            // Canceled carries no strike. The ordinary stage then pauses, since a further attempt
            // would return instantly on every track in the queue; the spend-down holds itself.
            if (result.IsAgentUnavailable)
            {
                jobs.Record(
                    item.VideoId,
                    JobStage.Verdict,
                    JobStatus.Canceled,
                    error: result.Error
                );

                // Another endpoint's limit or outage passes by itself, so it holds and retries;
                // a spent Claude allowance lasts hours, so it pauses (ADR 0067).
                if (!backfill && !_spendsClaudeQuota)
                    return StageTick.Held(
                        result.Error ?? "the judge's endpoint is unavailable",
                        result.RetryAfter
                    );

                if (!backfill)
                    control.Pause();

                return StageTick.AgentUnavailable(order, result.Error);
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
