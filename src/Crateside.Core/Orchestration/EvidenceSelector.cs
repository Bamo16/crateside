using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Decides which track to detect next. Split from the runner for the same reason
/// <see cref="SeparationSelector"/> is: this is the part with the rule in it, and it can be
/// tested without a network, a download or a detector.
/// </summary>
public sealed class EvidenceSelector(
    JobStore jobs,
    WorkingSet workingSet,
    int maxStrikes = EvidenceSelector.DefaultMaxStrikes
)
{
    /// <summary>
    /// How many failures before a track is left alone. More patient than separation's three,
    /// because an evidence run costs a download and some CPU rather than GPU hours, and its
    /// usual failures — a flaky fetch, a transient upstream error — are the retryable kind.
    /// </summary>
    public const int DefaultMaxStrikes = 5;

    /// <summary>
    /// The next track to detect: oldest-added first, skipping anything already detected, in
    /// flight, or given up on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It reads <see cref="QueueView.Outstanding"/> — tracks with no verdict — which is the
    /// right list precisely because <em>Outstanding</em> means "not judged yet" and evidence is
    /// what a verdict is made from. Held tracks are structurally absent from it.
    /// </para>
    /// <para>
    /// <strong>Evidence already in the working set is what makes this terminate.</strong> A
    /// detected track is still outstanding — detection records nothing in the ledger — so
    /// without this the same track would be picked forever and no other would ever run.
    /// </para>
    /// </remarks>
    public bool TryPickNext(QueueView view, out QueueItem next)
    {
        next = view.Outstanding.FirstOrDefault(i =>
            !i.IsUnavailable && !workingSet.HasEvidence(i.VideoId) && IsEligible(i)
        )!;

        return next is not null;
    }

    /// <remarks>
    /// A past success is no reason to refuse: the working-set check above has already said the
    /// evidence is not there, so this is a re-acquire rather than a repeat. The working set is
    /// scratch and may be deleted by hand at any time.
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
