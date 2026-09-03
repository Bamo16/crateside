using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>
/// crateside's instruction for one track: which URL is canonical, where its stems belong, and
/// what to ask for. What used to be prose in the detect-key skill.
/// </summary>
/// <remarks>
/// A work order says what <em>should</em> happen to a track next. It is not a record that
/// anything did — that is <see cref="TrackJob"/>. Composing one is pure: it reads the queue item
/// and the ledger and touches nothing.
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
    /// <remarks>
    /// <para>
    /// <strong>Destination follows the verdict</strong>, so the library itself records which
    /// tracks are safe to pair. Only HIGH and MEDIUM produce a key the user would tag with, so
    /// LOW and WITHHELD route to <see cref="StemDestination.UnresolvedKey"/> — which is exactly
    /// what that folder is for, since the bare stems root carries no signal about whether a key
    /// was resolved.
    /// </para>
    /// <para>
    /// <strong>A track with no verdict at all never reaches separation</strong>, so it cannot be
    /// routed wrongly: <see cref="SeparationSelector"/> requires a ledger line, because nothing
    /// moves stems afterwards and a track separated ahead of its verdict would sit in
    /// <see cref="StemDestination.UnresolvedKey"/> permanently even once a HIGH verdict arrived
    /// (ADR 0005). Composing an order for an earlier stage still yields a destination; it simply
    /// means nothing until separation is the stage asking.
    /// </para>
    /// <para>
    /// <strong>The URL choice is a choice of form, not of song.</strong> An art track is a
    /// YouTube Music track, so it gets the Music URL the skill prefers. Anything else gets the
    /// ordinary watch URL — which is <em>not</em> a claim that no better version exists; that is
    /// what <see cref="Advisory"/> is for, and finding a different video ID for the same song
    /// stays a human call.
    /// </para>
    /// </remarks>
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

/// <summary>
/// Which of the two stem folders a track's output belongs in. The split is the library's own
/// record of what is safe to pair, so there is no third option and no neutral one.
/// </summary>
public enum StemDestination
{
    /// <summary>A verdict the user would tag with — HIGH or MEDIUM.</summary>
    KeyIdentified,

    /// <summary>LOW, WITHHELD, or not yet judged.</summary>
    UnresolvedKey,
}

/// <summary>What to ask the separator for: which stems, in what form.</summary>
/// <remarks>
/// <para>
/// <strong>A plan, never a fact.</strong> Nobody chooses a recipe — it is derived from what is
/// known about the release, which is why <see cref="For"/> takes a queue item rather than being
/// set on the order. Recording the fact and deriving the plan is ADR 0004's rule, and it is what
/// lets the plan change later without the record becoming a lie.
/// </para>
/// <para>
/// Deriving it through <see cref="For"/> rather than defaulting a property is also the seam for a
/// per-track <strong>recipe override</strong> — a user who wants <c>vocal_clean</c> for one track
/// for no reason a fact could express. That is a queue instruction this method would prefer over
/// the derivation, and nothing else moves.
/// </para>
/// </remarks>
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
    /// stem and <strong>the source track is the sample</strong> — which is what
    /// <see cref="KeepSource"/> is for.
    /// </summary>
    /// <remarks>
    /// Who <em>places</em> that source moved once crateside began separating the file it had
    /// already downloaded: <c>--keep-source</c> is a flag about URL inputs, so it does nothing
    /// for a local one, and crateside copies the source into the destination itself.
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
    /// <remarks>
    /// <para>
    /// Counted rather than matched by name, deliberately. The names belong to StemForge
    /// (<c>Vocal - Full</c> against crateside's <c>vocal_full</c>) and mapping between them
    /// would make a rename over there a silent miscount over here. A count encodes nothing about
    /// the other tool's vocabulary.
    /// </para>
    /// <para>
    /// Repeated presets are not de-duplicated, because they run as independent stem sets and
    /// each produces a file.
    /// </para>
    /// </remarks>
    public int ExpectedOutputs => Presets.Count + (ExtractDrums ? 1 : 0) + (KeepSource ? 1 : 0);

    /// <summary>The recipe a track's release calls for.</summary>
    public static SeparationRecipe For(QueueItem item) => For(item.IsInstrumentalRelease);

    /// <summary>
    /// The same rule off the one fact it turns on, for a reader holding the fact and not the queue
    /// item it came from.
    /// </summary>
    public static SeparationRecipe For(bool isInstrumentalRelease) =>
        isInstrumentalRelease ? InstrumentalRelease : Standard;
}
