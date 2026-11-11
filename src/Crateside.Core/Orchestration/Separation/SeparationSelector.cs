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
            .. Candidates(view)
                .Where(candidate => candidate.Selection is Selection.Picked)
                .Select(candidate => candidate.Item),
        ];

    /// <summary>Every track waiting for separation, once each, with why it is or is not in the line.</summary>
    public IReadOnlyList<Decision> Decisions(WorkListsView view) =>
        [
            .. Candidates(view)
                .Select(candidate => new Decision(
                    candidate.Item.TrackKey,
                    candidate.Item.Title,
                    candidate.Selection
                )),
        ];

    private IEnumerable<(WorkItem Item, Selection Selection)> Candidates(WorkListsView view) =>
        view
            .AwaitingSeparation.DistinctBy(item => item.TrackKey, StringComparer.Ordinal)
            .Select(item => (item, Selection.From(WhyNot(item))));

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
    private SkipCause? WhyNot(WorkItem item) =>
        item.IsUnavailable ? SkipCause.Unavailable
        : CutRequests.OwesCut(item, jobs) ? null
        : StageEligibility.WhyNot(
            jobs,
            item.TrackKey,
            JobStage.Separation,
            maxStrikes,
            retrySucceeded: false
        );
}
