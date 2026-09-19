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
    /// The files meant to be kept and tagged.
    /// </summary>
    /// <remarks>
    /// For most songs the vocal and instrumental stems; for an instrumental release, the source
    /// track (ADR 0004).
    /// </remarks>
    public IReadOnlyList<StagingStem> Samples => [.. Stems.Where(s => !s.IsWarpGuide)];

    /// <summary>
    /// Warp guides still on disk, whose presence means warping is outstanding.
    /// </summary>
    /// <remarks>This says only that a guide is still here, not that its absence means warped.</remarks>
    public IReadOnlyList<StagingStem> WarpGuides => [.. Stems.Where(s => s.IsWarpGuide)];

    public int TaggedStemCount => Samples.Count(s => s.IsKeyTagged);

    /// <summary>
    /// True when every <em>sample</em> carries a key; warp guides are excluded.
    /// </summary>
    /// <remarks>
    /// Excluded on retention, not on tagging: crateside tags a guide like anything else.
    /// </remarks>
    public bool IsFullyTagged => Samples.Count > 0 && TaggedStemCount == Samples.Count;

    /// <summary>
    /// Stems tagged in Ableton with a key that is not the ledger's current verdict.
    /// </summary>
    /// <remarks>
    /// The tag is what pairing uses, so a stale one silently misroutes mashups.
    /// </remarks>
    public IReadOnlyList<StagingStem> StaleTaggedStems =>
        [.. Stems.Where(s => s.KeyDisagreesWithVerdict)];
}

/// <summary>One staged file of a song: a sample to be tagged, or a warp guide to be used up.</summary>
public sealed record StagingStem
{
    public required string Path { get; init; }
    public required string FileName { get; init; }

    /// <summary>
    /// The StemForge preset, or null when the file is a verbatim source download.
    /// </summary>
    /// <remarks>
    /// StemForge tags a source download too, but deliberately without a preset descriptor.
    /// </remarks>
    public string? Preset { get; init; }

    /// <summary>
    /// What the user calls this file when crateside adopted it rather than made it (ADR 0022).
    /// </summary>
    /// <remarks>
    /// Must reach every reader that decides what a re-cut may delete: an adopted file carries no
    /// preset, so anything seeing only <see cref="Preset"/> reads it as the separation source.
    /// </remarks>
    public string? AdoptedAs { get; init; }

    /// <summary>crateside took this file in rather than making it.</summary>
    public bool IsAdopted => AdoptedAs is not null;

    /// <summary>
    /// A stem made to be <em>looked at</em> rather than heard: the drums, deleted once warping
    /// is done.
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
    public string Label => Preset ?? AdoptedAs ?? "source";
}
