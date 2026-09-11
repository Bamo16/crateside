using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>Decides which track to judge next.</summary>
public sealed class VerdictSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = VerdictSelector.DefaultMaxStrikes
)
{
    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track to judge: oldest-added first, skipping anything already judged, not yet
    /// detected, in flight, given up on, or stopped for a premium shortfall.
    /// </summary>
    /// <remarks>
    /// This predicate is the exact negation of what <see cref="VerdictRunner"/> checks
    /// afterward, which is why a half-finished session needs no special handling. The ledger
    /// check re-verifies a <see cref="QueueView.Outstanding"/> snapshot that may be stale, since
    /// this stage writes the very thing the snapshot measures.
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
                // A prior success here produced nothing that survived the ledger check, so
                // retrying is the only way the track moves.
                retrySucceeded: true
            )
            // Last, because it opens a file: a swap copies the verdict forward, so judging poor
            // audio first would restate that key onto the better upload rather than re-derive it.
            && workingSet.AcquisitionFor(i.VideoId)?.Stops is not true
        )!;

        return next is not null;
    }
}
