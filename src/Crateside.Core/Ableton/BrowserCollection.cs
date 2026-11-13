using Crateside.Core.Ledger;
using Crateside.Core.Marks;

namespace Crateside.Core.Ableton;

/// <summary>What a browser Collection can mean to crateside; which of Live's numbers carries it is the user's choice (#166).</summary>
public enum BrowserCollection
{
    CheckBeat,
    Warped,
    WarpByHand,
    KeyCaveat,
    OddBars,
}

/// <summary>Which meanings crateside sets, and which a stem's state takes away.</summary>
public static class BrowserCollections
{
    /// <summary>One per staged stem, and none once it is imported.</summary>
    public static IReadOnlySet<BrowserCollection> WarpStates { get; } =
        new HashSet<BrowserCollection>
        {
            BrowserCollection.CheckBeat,
            BrowserCollection.Warped,
            BrowserCollection.WarpByHand,
        };

    /// <summary>Marks on every stem of a track, in staging and the library alike.</summary>
    public static IReadOnlySet<BrowserCollection> TrackMarks { get; } =
        new HashSet<BrowserCollection> { BrowserCollection.KeyCaveat, BrowserCollection.OddBars };

    /// <summary>Every meaning crateside sets and takes away; a number mapped to none of them is the user's.</summary>
    public static IReadOnlySet<BrowserCollection> Cratesides { get; } =
        new HashSet<BrowserCollection>(WarpStates.Concat(TrackMarks));

    /// <summary>Key caveat when the verdict's Key tag may mislead a pairing or the maintainer marked the key as changing; Odd bars when they marked the track so.</summary>
    public static IReadOnlyList<BrowserCollection> TrackMarksFor(
        LedgerRun? verdict,
        IReadOnlyCollection<TrackMark>? marked = null
    ) =>
        [
            .. MayMislead(verdict) || marked?.Contains(TrackMark.ChangesKey) is true
                ? [BrowserCollection.KeyCaveat]
                : Array.Empty<BrowserCollection>(),
            .. marked?.Contains(TrackMark.OddBars) is true
                ? [BrowserCollection.OddBars]
                : Array.Empty<BrowserCollection>(),
        ];

    // Modal, LOW or withheld, or a section in another center that something independent of the judge places (ADR 0095).
    private static bool MayMislead(LedgerRun? verdict) =>
        verdict
            is { IsCollapsed: true }
                or { Confidence: KeyConfidence.Low or KeyConfidence.Withheld }
        || verdict is { Sections: var sections } && sections.Any(section => section.IsConfirmed);
}
