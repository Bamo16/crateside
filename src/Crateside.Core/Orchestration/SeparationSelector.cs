using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>Decides which track to separate next.</summary>
public sealed class SeparationSelector(
    JobStore jobs,
    int maxStrikes = SeparationSelector.DefaultMaxStrikes
)
{
    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The next track to separate: oldest-added first, skipping anything already done, in
    /// flight, or given up on.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="QueueView.AwaitingSeparation"/>, not <see cref="QueueView.Outstanding"/>:
    /// separation needs a verdict's recipe and destination. LOW and WITHHELD still count as one.
    /// </remarks>
    public bool TryPickNext(QueueView view, KeyLedger ledger, out WorkOrder order)
    {
        if (
            view.AwaitingSeparation.FirstOrDefault(i => !i.IsUnavailable && IsEligible(i))
            is not { } next
        )
        {
            order = null!;

            return false;
        }

        order = WorkOrder.Compose(next, ledger);

        return true;
    }

    /// <remarks>
    /// <paramref name="retrySucceeded"/> is false here, unlike the other selectors: a recorded
    /// success with no stems on disk most likely means they were deleted on purpose.
    /// </remarks>
    private bool IsEligible(QueueItem item) =>
        StageEligibility.Allows(
            jobs,
            item.VideoId,
            JobStage.Separation,
            maxStrikes,
            retrySucceeded: false
        );
}
