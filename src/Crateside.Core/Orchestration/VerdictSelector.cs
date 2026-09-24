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
    /// The next track to judge, oldest-added first, skipping anything the ledger (not the view)
    /// has judged, undetected, in flight, given up on, or parked for a premium shortfall.
    /// </summary>
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
