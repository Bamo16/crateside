using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Decides which track to judge next.</summary>
public sealed class VerdictSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = VerdictSelector.DefaultMaxStrikes,
    ResolvedNameStore? names = null,
    IOptions<JudgeOptions>? judge = null
)
{
    private readonly bool _hasJudge = judge?.Value.IsConfigured ?? true;

    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track to judge, oldest-added first, skipping anything the ledger (not the view)
    /// has judged, undetected, in flight, given up on, parked, or looking like a judged song.
    /// The library backfill waits for the weekly spend-down unless the user asked for it (ADR 0063).
    /// </summary>
    /// <param name="skip">Tracks to pass over, as the rule pass does with those it has tried.</param>
    public bool TryPickNext(
        WorkListsView view,
        KeyLedger ledger,
        out WorkItem next,
        IReadOnlySet<string>? skip = null
    )
    {
        next = view.Outstanding.FirstOrDefault(i =>
            (!i.IsBackfill || i.TrackKey == view.Bumped)
            && skip?.Contains(i.TrackKey) is not true
            && IsJudgeable(i, ledger)
        )!;

        return next is not null;
    }

    /// <summary>This one track, if it is outstanding and judgeable.</summary>
    public bool TryPick(WorkListsView view, KeyLedger ledger, string trackKey, out WorkItem next)
    {
        next = view.Outstanding.FirstOrDefault(i =>
            i.TrackKey == trackKey && IsJudgeable(i, ledger)
        )!;

        return next is not null;
    }

    /// <summary>The next library backfill track, for the spend-down to judge; never anything else.</summary>
    public bool TryPickBackfill(WorkListsView view, KeyLedger ledger, out WorkItem next)
    {
        next = view.Outstanding.FirstOrDefault(i => i.IsBackfill && IsJudgeable(i, ledger))!;

        return next is not null;
    }

    /// <summary>
    /// What the spend-down judges: new intake first (unless judging is paused), then a song owed a
    /// re-judge, then the library backfill.
    /// </summary>
    public bool TryPickIdle(
        WorkListsView view,
        KeyLedger ledger,
        IReadOnlyList<LedgerRun> owed,
        bool includeIntake,
        out WorkItem next,
        out bool isRejudge
    )
    {
        isRejudge = false;

        if (includeIntake && TryPickNext(view, ledger, out next))
            return true;

        if (TryPickRejudge(owed, out next))
        {
            isRejudge = true;

            return true;
        }

        return TryPickBackfill(view, ledger, out next);
    }

    private bool TryPickRejudge(IReadOnlyList<LedgerRun> owed, out WorkItem next)
    {
        next = owed.Select(run => new WorkItem
            {
                TrackKey = run.SourceTrackKey ?? string.Empty,
                Title = run.DisplayName,
            })
            .FirstOrDefault(item =>
                item.TrackKey is not ""
                && StageEligibility.Allows(
                    jobs,
                    item.TrackKey,
                    JobStage.Verdict,
                    maxStrikes,
                    retrySucceeded: true
                )
            )!;

        return next is not null;
    }

    private bool IsJudgeable(WorkItem i, KeyLedger ledger) =>
        !i.IsUnavailable
        && workingSet.HasEvidence(i.TrackKey)
        && !ledger.HasRunFor(i.TrackKey)
        && (
            jobs.TryGet(i.TrackKey, JobStage.Verdict, out var job)
            && job.Status is JobStatus.AwaitingPerson
                ? _hasJudge
                : StageEligibility.Allows(
                    jobs,
                    i.TrackKey,
                    JobStage.Verdict,
                    maxStrikes,
                    // A prior success here produced nothing that survived the ledger check, so
                    // retrying is the only way the track moves.
                    retrySucceeded: true
                )
        )
        // Last, because it opens a file: a swap copies the verdict forward, so judging poor
        // audio first would restate that key onto the better upload rather than re-derive it.
        && workingSet.AcquisitionFor(i.TrackKey)?.Stops is not true
        && LookalikeOf(i, ledger, names?.Read().GetValueOrDefault(i.TrackKey)?.DisplayTitle)
            is null;

    /// <summary>
    /// The judged song this track's name, or StemForge's name for it once resolved, looks like,
    /// unless the user has said it is not that one (#41).
    /// </summary>
    public static LedgerRun? LookalikeOf(WorkItem item, KeyLedger ledger, string? resolvedName) =>
        Lookalikes.TryFind(resolvedName ?? item.Title, ledger, out var song)
        && !song.Song.Equals(item.DifferentFrom, StringComparison.OrdinalIgnoreCase)
            ? song
            : null;
}
