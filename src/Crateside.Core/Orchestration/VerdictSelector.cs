using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>Decides which track to judge next.</summary>
/// <param name="resolvedName">What StemForge will call a track, or null before it has said.</param>
public sealed class VerdictSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = VerdictSelector.DefaultMaxStrikes,
    Func<string, string?>? resolvedName = null
)
{
    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track to judge, oldest-added first, skipping anything the ledger (not the view)
    /// has judged, undetected, in flight, given up on, parked, or looking like a judged song.
    /// The library backfill waits for an idle window unless the user asked for it (ADR 0035).
    /// </summary>
    public bool TryPickNext(QueueView view, KeyLedger ledger, out QueueItem next)
    {
        next = view.Outstanding.FirstOrDefault(i =>
            (!i.IsBackfill || i.VideoId == view.Bumped) && IsJudgeable(i, ledger)
        )!;

        return next is not null;
    }

    /// <summary>The next library backfill track, for an idle window to judge; never anything else.</summary>
    public bool TryPickBackfill(QueueView view, KeyLedger ledger, out QueueItem next)
    {
        next = view.Outstanding.FirstOrDefault(i => i.IsBackfill && IsJudgeable(i, ledger))!;

        return next is not null;
    }

    private bool IsJudgeable(QueueItem i, KeyLedger ledger) =>
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
        && LookalikeOf(i, ledger, resolvedName) is null;

    /// <summary>
    /// The judged song this track's name looks like, unless the user has said it is not that one
    /// — a question to answer before any quota is spent on it (#41).
    /// </summary>
    public static LedgerRun? LookalikeOf(
        QueueItem item,
        KeyLedger ledger,
        Func<string, string?>? resolvedName
    ) =>
        Lookalikes.TryFind(resolvedName?.Invoke(item.VideoId) ?? item.Title, ledger, out var song)
        && !song.Song.Equals(item.DifferentFrom, StringComparison.OrdinalIgnoreCase)
            ? song
            : null;
}
