using Crateside.Core.Ledger;

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
    // Odd bars joins once a track can be marked as having a bar that gains or drops beats (#86).
    public static IReadOnlySet<BrowserCollection> TrackMarks { get; } =
        new HashSet<BrowserCollection> { BrowserCollection.KeyCaveat };

    /// <summary>Every Collection crateside sets and takes away; one it does not own is the user's.</summary>
    public static IReadOnlySet<BrowserCollection> Cratesides { get; } =
        new HashSet<BrowserCollection>(WarpStates.Concat(TrackMarks));

    /// <summary>Key caveat when the verdict's Key tag may mislead a pairing: modal, LOW or withheld.</summary>
    public static IReadOnlyList<BrowserCollection> TrackMarksFor(LedgerRun? verdict) =>
        verdict
            is { IsCollapsed: true }
                or { Confidence: KeyConfidence.Low or KeyConfidence.Withheld }
            ? [BrowserCollection.KeyCaveat]
            : [];

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
}
