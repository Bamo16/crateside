using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>
/// crateside's instruction for one track: which URL is canonical, where its stems belong, and
/// what to ask for. What used to be prose in the detect-key skill.
/// </summary>
/// <remarks>
/// Not a record that anything did — that is <see cref="TrackJob"/>. Composing one is pure.
/// </remarks>
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

    /// <summary>What to ask the separator for, derived from what is known about the release.</summary>
    public SeparationRecipe Recipe { get; init; } = SeparationRecipe.Standard;

    /// <summary>
    /// Whether the user has stated the release has no vocals, for the verdict run to pass
    /// through to the ledger. Null means nobody said, which is not "it has vocals".
    /// </summary>
    /// <remarks>
    /// Only ever <c>false</c> or null: crateside carries the user's statement that a release is
    /// instrumental, and has nothing to say about a track they have not spoken about.
    /// </remarks>
    public bool? HasVocals { get; init; }

    /// <summary>
    /// What is worth saying to the user before this runs, or null when nothing is.
    /// </summary>
    public string? Advisory => PremiumOutlookReader.AdvisoryFor(PremiumOutlook);

    /// <summary>
    /// Decides what should happen to a queued track.
    /// </summary>
    public static WorkOrder Compose(QueueItem item, KeyLedger ledger) =>
        new()
        {
            VideoId = item.VideoId,
            Title = item.Title,
            SourceUrl =
                item.PremiumOutlook is PremiumOutlook.ArtTrack
                    ? $"https://music.youtube.com/watch?v={item.VideoId}"
                    : item.WatchUrl,
            Destination =
                ledger.TryGetLatestByVideoId(item.VideoId, out var run) && run.HasTaggableKey
                    ? StemDestination.KeyIdentified
                    : StemDestination.UnresolvedKey,
            PremiumOutlook = item.PremiumOutlook,
            Recipe = SeparationRecipe.For(item),
            HasVocals = item.IsInstrumentalRelease ? false : null,
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
    public static SeparationRecipe Standard { get; } =
        new() { Presets = ["vocal_full", "instrumental_full"], ExtractDrums = true };

    /// <summary>
    /// For a release that has no vocals: nothing to separate out, so the drums are the only
    /// stem and the source track is the sample — which is what <see cref="KeepSource"/> is for.
    /// </summary>
    /// <remarks>
    /// <c>--keep-source</c> is a flag about URL inputs, so it does nothing for a local file;
    /// crateside copies the source into the destination itself instead.
    /// </remarks>
    public static SeparationRecipe InstrumentalRelease { get; } =
        new()
        {
            Presets = [],
            ExtractDrums = true,
            KeepSource = true,
        };

    public required IReadOnlyList<string> Presets { get; init; }
    public required bool ExtractDrums { get; init; }

    /// <summary>Whether the source track is itself a keeper, and belongs in the destination.</summary>
    public bool KeepSource { get; init; }

    public string Format { get; init; } = "flac";

    /// <summary>
    /// How many files this recipe should produce — one per preset, plus the warp guide and the
    /// kept source where they are asked for.
    /// </summary>
    public int ExpectedOutputs => Presets.Count + (ExtractDrums ? 1 : 0) + (KeepSource ? 1 : 0);

    /// <summary>
    /// How many passes the separator makes. A kept source is not one of them: it is copied.
    /// </summary>
    public int Passes => Presets.Count + (ExtractDrums ? 1 : 0);

    /// <summary>
    /// Roughly how long the GPU will be busy.
    /// </summary>
    /// <remarks>
    /// A shape assumed, not measured — good enough to say roughly how long, not to build logic on.
    /// </remarks>
    public TimeSpan Estimate => TimeSpan.FromSeconds(75 + (95 * Passes));

    /// <summary>The recipe a track's release calls for.</summary>
    public static SeparationRecipe For(QueueItem item) => For(item.IsInstrumentalRelease);

    /// <summary>
    /// The same rule off the one fact it turns on, for a reader holding the fact and not the queue
    /// item it came from.
    /// </summary>
    public static SeparationRecipe For(bool isInstrumentalRelease) =>
        isInstrumentalRelease ? InstrumentalRelease : Standard;
}
