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

    /// <summary>What to ask the separator for. Fixed today; carried so the order is complete.</summary>
    public SeparationRecipe Recipe { get; init; } = SeparationRecipe.Standard;

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
    /// everything else — LOW, WITHHELD, and <em>no verdict at all</em> — routes to
    /// <see cref="StemDestination.UnresolvedKey"/>. Stems with no verdict are explicitly that
    /// case, not an error: the bare stems root is never a valid destination for a track that
    /// went through this workflow, because a stem sitting there carries no signal about whether
    /// its key was resolved, which is the entire point of the split.
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

/// <summary>What to ask the separator for.</summary>
/// <remarks>
/// The set is always vocal + instrumental + drums. Repeated presets run as independent stem
/// sets rather than being blended, and multiple inputs loop serially inside one GPU-safe
/// process — so any number of tracks for one destination is a single invocation.
/// </remarks>
public sealed record SeparationRecipe
{
    public static SeparationRecipe Standard { get; } =
        new() { Presets = ["vocal_full", "instrumental_full"], ExtractDrums = true };

    public required IReadOnlyList<string> Presets { get; init; }
    public required bool ExtractDrums { get; init; }
    public string Format { get; init; } = "flac";
}
