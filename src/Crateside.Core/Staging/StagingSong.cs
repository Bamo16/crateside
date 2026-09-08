using Crateside.Core.Ledger;

namespace Crateside.Core.Staging;

/// <summary>
/// One row of the staging board: a song, with its stems rolled up.
/// </summary>
/// <remarks>Entirely derived — nothing here is stored, nothing marked done by hand.</remarks>
public sealed record StagingSong
{
    /// <summary>The ledger join key. Null for a stem carrying no usable provenance.</summary>
    public string? SourceVideoId { get; init; }

    /// <summary>What to call this row: the ledger's title, the tag's, or the filename.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The current verdict, or null when the ledger has never seen this song.</summary>
    public LedgerRun? Verdict { get; init; }

    /// <summary>The key to tag with, or null when withheld or unknown.</summary>
    public string? Key => Verdict?.HasTaggableKey is true ? Verdict.Key : null;

    public KeyConfidence Confidence => Verdict?.Confidence ?? KeyConfidence.Unknown;

    /// <summary>The warp starting point. Rough by design; not a tagged value.</summary>
    public double? Bpm => Verdict?.Bpm;

    public required IReadOnlyList<StagingStem> Stems { get; init; }

    /// <summary>True when the ledger has no run at all for this song — nothing to tag with.</summary>
    public bool AwaitingDetection => Verdict is null;

    /// <summary>
    /// The files meant to be kept and tagged. For most songs the vocal and instrumental stems;
    /// for an instrumental release, the source track, because there was never anything to
    /// separate out of it.
    /// </summary>
    public IReadOnlyList<StagingStem> Samples => [.. Stems.Where(s => !s.IsWarpGuide)];

    /// <summary>
    /// Warp guides still on disk. Their presence means warping is outstanding — deleting one is
    /// a step of the workflow, not tidying up.
    /// </summary>
    /// <remarks>This says only that a guide is still here, not that its absence means warped.</remarks>
    public IReadOnlyList<StagingStem> WarpGuides => [.. Stems.Where(s => s.IsWarpGuide)];

    public int TaggedStemCount => Samples.Count(s => s.IsKeyTagged);

    /// <summary>
    /// True when every <em>sample</em> carries a key — warp guides are excluded, because they are
    /// used up rather than kept.
    /// </summary>
    /// <remarks>
    /// Not because a warp guide goes untagged — crateside tags one like anything else. The axis
    /// is retention: a guide is excluded because it is going to be deleted.
    /// </remarks>
    public bool IsFullyTagged => Samples.Count > 0 && TaggedStemCount == Samples.Count;

    /// <summary>
    /// Stems tagged in Ableton with a key that is not the ledger's current verdict — which is
    /// what a supersede leaves behind. Worth surfacing: the tag is what pairing actually uses,
    /// so a stale one silently misroutes mashups.
    /// </summary>
    public IReadOnlyList<StagingStem> StaleTaggedStems =>
        [.. Stems.Where(s => s.KeyDisagreesWithVerdict)];
}

/// <summary>One staged file of a song: a sample to be tagged, or a warp guide to be used up.</summary>
public sealed record StagingStem
{
    public required string Path { get; init; }
    public required string FileName { get; init; }

    /// <summary>
    /// The StemForge preset — Vocal - Full, Instrumental - Full, Drums. Null when the file is a
    /// verbatim source download rather than a separation output: StemForge tags those too, but
    /// deliberately without a preset descriptor.
    /// </summary>
    public string? Preset { get; init; }

    /// <summary>
    /// A stem made to be <em>looked at</em> rather than heard — the drums, whose transients are
    /// what warp markers get placed against, and which is deleted once warping is done.
    /// </summary>
    /// <remarks>
    /// Every drums stem is treated as a warp guide, with no way to say otherwise — a kept drum
    /// stem has to be handled by hand, outside crateside.
    /// </remarks>
    public bool IsWarpGuide => IsWarpGuidePreset(Preset);

    /// <summary>The rule itself, so every reader of a preset decides this the same way.</summary>
    public static bool IsWarpGuidePreset(string? preset) =>
        preset?.Contains("drum", StringComparison.OrdinalIgnoreCase) is true;

    /// <summary>The key Ableton has on the file, recombined from its two keywords.</summary>
    public string? AbletonKey { get; init; }

    /// <summary>Tagged with a tonic but no mode — half a key, which is not a key.</summary>
    public bool HasPartialKey { get; init; }

    public bool IsKeyTagged => AbletonKey is not null;

    /// <summary>
    /// True when Ableton's key and the ledger's current verdict are both present and differ.
    /// </summary>
    public bool KeyDisagreesWithVerdict { get; init; }

    /// <summary>What to show in the stem column when there is no preset to show.</summary>
    public string Label => Preset ?? "source";
}
