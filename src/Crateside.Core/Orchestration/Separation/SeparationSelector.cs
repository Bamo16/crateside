using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Separation;

/// <summary>Decides which track to separate next.</summary>
public sealed class SeparationSelector(
    JobStore jobs,
    int maxStrikes = SeparationSelector.DefaultMaxStrikes
)
{
    /// <summary>How many failures before a track is left alone.</summary>
    public const int DefaultMaxStrikes = 3;

    /// <summary>
    /// The tracks separation will work, in the order it will take them: what the Tracks page shows
    /// as the GPU line.
    /// </summary>
    public IReadOnlyList<WorkItem> Line(WorkListsView view) =>
        [
            .. view
                .AwaitingSeparation.Where(item => !item.IsUnavailable && IsEligible(item))
                .DistinctBy(item => item.VideoId, StringComparer.Ordinal),
        ];

    /// <summary>
    /// The next judged track to separate, even LOW or WITHHELD: oldest-added first, skipping
    /// anything already done, in flight, or given up on.
    /// </summary>
    public bool TryPickNext(WorkListsView view, KeyLedger ledger, out WorkOrder order)
    {
        if (Line(view) is not [var next, ..])
        {
            order = null!;

            return false;
        }

        order = WorkOrder.Compose(next, ledger);

        return true;
    }

    /// <summary>
    /// Never retries a success unasked, unlike the other selectors: stems gone after one were most
    /// likely deleted on purpose. A re-cut or swap since then is the asking (#50).
    /// </summary>
    private bool IsEligible(WorkItem item) =>
        StageEligibility.Allows(
            jobs,
            item.VideoId,
            JobStage.Separation,
            maxStrikes,
            retrySucceeded: false
        ) || CutRequests.OwesCut(item, jobs);
}
