using Crateside.Core.Ledger;
using Crateside.Core.Marks;

namespace Crateside.Core.Ableton;

/// <summary>Live's seven browser Collections, by Live's number, named as the maintainer named them (#130).</summary>
public enum BrowserCollection
{
    Mine = 1,
    CheckBeat = 2,

    // No longer given (ADR 0054, amended 2026-10-19), but still crateside's, so a stem in it is taken out.
    PlaceBeat = 3,
    Warped = 4,
    WarpByHand = 5,
    KeyCaveat = 6,
    OddBars = 7,
}

/// <summary>Which Collections crateside sets, and how they join the ones the user put a file in.</summary>
public static class BrowserCollections
{
    /// <summary>One per staged stem, and none once it is imported.</summary>
    public static IReadOnlySet<BrowserCollection> WarpStates { get; } =
        new HashSet<BrowserCollection>
        {
            BrowserCollection.CheckBeat,
            BrowserCollection.PlaceBeat,
            BrowserCollection.Warped,
            BrowserCollection.WarpByHand,
        };

    /// <summary>Marks on every stem of a track, in staging and the library alike.</summary>
    public static IReadOnlySet<BrowserCollection> TrackMarks { get; } =
        new HashSet<BrowserCollection> { BrowserCollection.KeyCaveat, BrowserCollection.OddBars };

    /// <summary>Every Collection crateside sets and takes away; one it does not own is the user's.</summary>
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

    /// <summary>A file's Collections with those in <paramref name="replacing"/> swapped for <paramref name="wanted"/>.</summary>
    public static IReadOnlyList<int> Merged(
        IReadOnlyList<int> existing,
        IEnumerable<BrowserCollection> wanted,
        IReadOnlySet<BrowserCollection> replacing
    ) =>
        [
            .. existing
                .Where(number => !replacing.Contains((BrowserCollection)number))
                .Union(wanted.Select(collection => (int)collection))
                .Order(),
        ];

    // Modal, LOW or withheld, or sections the judge placed in different centres.
    private static bool MayMislead(LedgerRun? verdict) =>
        verdict
            is { IsCollapsed: true }
                or { Confidence: KeyConfidence.Low or KeyConfidence.Withheld }
        || verdict is { Sections: { Count: > 1 } sections }
            && sections.Select(section => section.Centre).Distinct().Count() > 1;
}
