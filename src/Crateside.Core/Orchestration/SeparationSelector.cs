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
    /// <para>
    /// Reads <see cref="QueueView.AwaitingSeparation"/> rather than
    /// <see cref="QueueView.Outstanding"/>: separation needs a ledger line's recipe
    /// (<c>has_vocals</c>) and destination (<c>HasTaggableKey</c>), neither of which exists
    /// before a verdict has run.
    /// </para>
    /// <para>
    /// A LOW or WITHHELD verdict still counts as a line, so a judged-but-unresolved track
    /// arrives here too, routed to <c>Unresolved Key</c>.
    /// </para>
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
    /// success with no stems on disk most likely means they were deleted on purpose, and
    /// redoing that would cost GPU hours against the user's wishes.
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
