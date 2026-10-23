using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Decides which track to judge next.</summary>
public sealed class VerdictSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = VerdictSelector.DefaultMaxStrikes,
    ResolvedNameStore? names = null
)
{
    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track to judge, oldest-added first, skipping anything the ledger (not the view)
    /// has judged, undetected, in flight, given up on, parked, or looking like a judged song.
    /// The library backfill waits for an idle window unless the user asked for it (ADR 0035).
    /// </summary>
    public bool TryPickNext(WorkListsView view, KeyLedger ledger, out WorkItem next)
    {
        next = view.Outstanding.FirstOrDefault(i =>
            (!i.IsBackfill || i.VideoId == view.Bumped) && IsJudgeable(i, ledger)
        )!;

        return next is not null;
    }

    /// <summary>This one track, if it is outstanding and judgeable.</summary>
    public bool TryPick(WorkListsView view, KeyLedger ledger, string videoId, out WorkItem next)
    {
        next = view.Outstanding.FirstOrDefault(i =>
            i.VideoId == videoId && IsJudgeable(i, ledger)
        )!;

        return next is not null;
    }

    /// <summary>The next library backfill track, for an idle window to judge; never anything else.</summary>
    public bool TryPickBackfill(WorkListsView view, KeyLedger ledger, out WorkItem next)
    {
        next = view.Outstanding.FirstOrDefault(i => i.IsBackfill && IsJudgeable(i, ledger))!;

        return next is not null;
    }

    /// <summary>
    /// What an idle window judges: new intake first (unless judging is paused), then a song owed a
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
                VideoId = run.SourceVideoId ?? string.Empty,
                Title = run.DisplayName,
            })
            .FirstOrDefault(item =>
                item.VideoId is not ""
                && StageEligibility.Allows(
                    jobs,
                    item.VideoId,
                    JobStage.Verdict,
                    maxStrikes,
                    retrySucceeded: true
                )
            )!;

        return next is not null;
    }

    private bool IsJudgeable(WorkItem i, KeyLedger ledger) =>
        !i.IsUnavailable
        && workingSet.HasEvidence(i.VideoId)
        && !ledger.HasRunFor(i.VideoId)
        && StageEligibility.Allows(
            jobs,
            i.VideoId,
            JobStage.Verdict,
            maxStrikes,
            // A prior success here produced nothing that survived the ledger check, so
            // retrying is the only way the track moves.
            retrySucceeded: true
        )
        // Last, because it opens a file: a swap copies the verdict forward, so judging poor
        // audio first would restate that key onto the better upload rather than re-derive it.
        && workingSet.AcquisitionFor(i.VideoId)?.Stops is not true
        && LookalikeOf(i, ledger, names?.Read().GetValueOrDefault(i.VideoId)?.DisplayTitle) is null;

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
