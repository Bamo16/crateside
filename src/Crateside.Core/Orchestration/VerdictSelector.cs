using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Decides which track to judge next. Split from the runner for the same reason the other
/// selectors are: this is the part with the rule in it, and it can be tested without spending
/// an agent session.
/// </summary>
public sealed class VerdictSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = VerdictSelector.DefaultMaxStrikes
)
{
    /// <summary>
    /// How many failures before a track is left alone.
    /// </summary>
    /// <remarks>
    /// Three, matching separation rather than evidence's five. A verdict run costs Claude quota,
    /// and its usual failures are not the transient kind — a track with no human coverage or
    /// genuinely ambiguous evidence fails the same way every time, and ADR 0001's point about a
    /// poisoned track is that it should stop being retried nightly.
    /// </remarks>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track to judge: oldest-added first, skipping anything already judged, not yet
    /// detected, in flight, or given up on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Evidence is the entry condition and a ledger line is the exit.</strong> A track
    /// with no evidence has nothing to judge, and one that already has a line has been judged —
    /// so this predicate is the exact negation of what
    /// <see cref="VerdictRunner"/> checks afterwards, which is what makes a half-finished
    /// session need no special handling.
    /// </para>
    /// <para>
    /// It reads <see cref="QueueView.Outstanding"/>, which is already "no verdict yet", so the
    /// ledger check is belt to that braces: <em>Outstanding</em> is computed against a disk
    /// scan taken at read time, and this stage writes the very thing that scan measures.
    /// </para>
    /// </remarks>
    public bool TryPickNext(QueueView view, KeyLedger ledger, out QueueItem next)
    {
        next = view.Outstanding.FirstOrDefault(i =>
            !i.IsUnavailable
            && workingSet.HasEvidence(i.VideoId)
            && !ledger.HasRunFor(i.VideoId)
            && StageEligibility.Allows(
                jobs,
                i.VideoId,
                JobStage.Verdict,
                maxStrikes,
                // The ledger check above has already said no verdict was recorded, so a job that
                // claims success produced nothing that survived. Judging again is the only way
                // that track ever moves.
                retrySucceeded: true
            )
        )!;

        return next is not null;
    }
}
