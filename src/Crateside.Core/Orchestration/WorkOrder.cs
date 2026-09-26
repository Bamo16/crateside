using Crateside.Core.Ledger;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration;

/// <summary>
/// crateside's instruction for one track: which URL is canonical, where its stems belong, and
/// what to ask for. What actually ran is a <see cref="TrackJob"/>.
/// </summary>
public sealed record WorkOrder
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }

    /// <summary>The URL to hand the downloader. See <see cref="Compose"/> for how it is picked.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>Which stem folder this track's output belongs in.</summary>
    public required StemDestination Destination { get; init; }

    /// <summary>The pre-flight quality hint, carried so the caller can surface it.</summary>
    public PremiumOutlook PremiumOutlook { get; init; }

    /// <summary>What to ask the separator for: the user's plan, rendered as a recipe.</summary>
    public SeparationRecipe Recipe { get; init; } = SeparationRecipe.Standard;

    /// <summary>
    /// Decides what should happen to a queued track.
    /// </summary>
    public static WorkOrder Compose(QueueItem item, KeyLedger ledger) =>
        new()
        {
            VideoId = item.VideoId,
            Title = item.Title,
            SourceUrl = item switch
            {
                { StatedSourceUrl: { Length: > 0 } stated } => stated,
                { PremiumOutlook: PremiumOutlook.ArtTrack } =>
                    $"https://music.youtube.com/watch?v={item.VideoId}",
                _ => item.WatchUrl,
            },
            Destination =
                ledger.TryGetLatestByVideoId(item.VideoId, out var run) && run.HasTaggableKey
                    ? StemDestination.KeyIdentified
                    : StemDestination.UnresolvedKey,
            PremiumOutlook = item.PremiumOutlook,
            Recipe = SeparationRecipe.For(item.Plan, item.Inventory),
        };
}

/// <summary>Which of the two stem folders a track's output belongs in.</summary>
public enum StemDestination
{
    /// <summary>A verdict the user would tag with — HIGH or MEDIUM.</summary>
    KeyIdentified,

    /// <summary>LOW, WITHHELD, or not yet judged.</summary>
    UnresolvedKey,
}

/// <summary>What to ask the separator for: which stems, in what form.</summary>
public sealed record SeparationRecipe
{
    /// <summary>
    /// The usual pair plus a warp guide. Repeated presets run as independent stem sets rather
    /// than being blended.
    /// </summary>
    public static SeparationRecipe Standard { get; } = From(StemPlan.Default);

    /// <summary>
    /// For a release that has no vocals: nothing to separate out, so the drums are the only
    /// stem and the source track is the sample — which is what <see cref="KeepSource"/> is for.
    /// </summary>
    public static SeparationRecipe InstrumentalRelease { get; } =
        From(StemPlan.InstrumentalRelease);

    public required IReadOnlyList<string> Presets { get; init; }
    public required bool ExtractDrums { get; init; }

    /// <summary>Whether the source track is itself a keeper, and belongs in the destination.</summary>
    public bool KeepSource { get; init; }

    public string Format { get; init; } = "flac";

    /// <summary>
    /// How many files the separator should produce — one per preset, plus the warp guide. A kept
    /// source is not among them: the download stage copies it (ADR 0019).
    /// </summary>
    public int ExpectedOutputs => Presets.Count + (ExtractDrums ? 1 : 0);

    /// <summary>
    /// How many passes the separator makes. A kept source is not one of them: it is copied.
    /// </summary>
    public int Passes => Presets.Count + (ExtractDrums ? 1 : 0);

    /// <summary>
    /// Roughly how long the GPU will be busy — an assumed shape, not a measurement, so build no
    /// logic on it.
    /// </summary>
    public TimeSpan Estimate => TimeSpan.FromSeconds(75 + (95 * Passes));

    /// <summary>
    /// Two recipes asking for the same files in the same order are the same recipe, where a record
    /// would compare <see cref="Presets"/> by reference.
    /// </summary>
    public bool Equals(SeparationRecipe? other) =>
        other is not null
        && ExtractDrums == other.ExtractDrums
        && KeepSource == other.KeepSource
        && Format == other.Format
        && Presets.SequenceEqual(other.Presets, StringComparer.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(ExtractDrums, KeepSource, Format, Presets.Count);

    /// <summary>
    /// What to ask the separator for, given what the user asked for (ADR 0018).
    /// </summary>
    public static SeparationRecipe From(StemPlan plan) => For(plan, StemInventory.Empty);

    /// <summary>
    /// What to ask the separator for, given what the user asked for and what is already made.
    /// </summary>
    public static SeparationRecipe For(StemPlan plan, StemInventory inventory)
    {
        var shortfall = inventory.Shortfall(plan);

        return new SeparationRecipe
        {
            Presets = shortfall.Presets,
            // A guide is for warping new samples by eye, so new presets get one. A re-downloaded
            // source does not: it is the same audio as the stems already warped, whose markers
            // ADR 0020 copies verbatim. With no sample on disk to copy from it is the guide or
            // nothing, which is what an instrumental release needs (#30).
            ExtractDrums =
                !inventory.HasWarpGuide
                && (
                    shortfall.Presets.Count > 0
                    || (shortfall.KeepSource && inventory is { Presets.Count: 0, HasSource: false })
                ),
            KeepSource = shortfall.KeepSource,
        };
    }
}
