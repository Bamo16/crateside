using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>Decides which track to detect next.</summary>
public sealed class EvidenceSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = EvidenceSelector.DefaultMaxStrikes
)
{
    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 5;

    /// <summary>
    /// The next track to detect: oldest-added first, skipping anything already detected, in
    /// flight, or given up on.
    /// </summary>
    /// <remarks>
    /// Evidence already in the working set is what makes this terminate: detection records
    /// nothing in the ledger, so a detected track is still outstanding, and without this check
    /// the same track would be picked forever.
    /// </remarks>
    public bool TryPickNext(QueueView view, out QueueItem next)
    {
        next = view.Outstanding.FirstOrDefault(i =>
            !i.IsUnavailable
            && !workingSet.HasEvidence(i.VideoId)
            // Not this stage's turn until the download stage has been (ADR 0019). Picking one up
            // early costs it a strike for arriving before its audio, which is not a fault of the
            // track and would eventually make it give up.
            && workingSet.TryGetSource(i.VideoId, out _)
            && IsEligible(i)
        )!;

        return next is not null;
    }

    /// <remarks>
    /// retrySucceeded is true here: the working-set check above already confirmed the evidence
    /// is gone, and the working set may be deleted by hand at any time, so a past success is not
    /// a reason to refuse a re-acquire.
    /// </remarks>
    private bool IsEligible(QueueItem item) =>
        StageEligibility.Allows(
            jobs,
            item.VideoId,
            JobStage.Evidence,
            maxStrikes,
            retrySucceeded: true
        );
}
