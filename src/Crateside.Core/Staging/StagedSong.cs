using Crateside.Core.Ledger;

namespace Crateside.Core.Staging;

/// <summary>
/// One song's files in Sample Staging, rolled up. Entirely derived, nothing stored.
/// </summary>
public sealed record StagedSong
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

    public required IReadOnlyList<StagedFile> Stems { get; init; }

    /// <summary>True when the ledger has no run at all for this song — nothing to tag with.</summary>
    public bool AwaitingDetection => Verdict is null;

    /// <summary>
    /// The files meant to be kept and tagged — usually stems, but for an instrumental release the
    /// source track (ADR 0004).
    /// </summary>
    public IReadOnlyList<StagedFile> Samples => [.. Stems.Where(s => !s.IsWarpGuide)];

    /// <summary>
    /// Warp guides still on disk, whose presence means warping is outstanding; their absence means
    /// nothing.
    /// </summary>
    public IReadOnlyList<StagedFile> WarpGuides => [.. Stems.Where(s => s.IsWarpGuide)];

    public int TaggedStemCount => Samples.Count(s => s.IsKeyTagged);

    /// <summary>
    /// True when every <em>sample</em> carries a key — warp guides excluded because they are not
    /// kept, though they are tagged too.
    /// </summary>
    public bool IsFullyTagged => Samples.Count > 0 && TaggedStemCount == Samples.Count;

    /// <summary>
    /// Stems tagged in Ableton with a key that is not the ledger's current verdict, which silently
    /// misroutes pairing.
    /// </summary>
    public IReadOnlyList<StagedFile> StaleTaggedStems =>
        [.. Stems.Where(s => s.KeyDisagreesWithVerdict)];
}

/// <summary>One staged file of a song: a sample to be tagged, or a warp guide to be used up.</summary>
public sealed record StagedFile
{
    public required string Path { get; init; }
    public required string FileName { get; init; }

    /// <summary>
    /// The StemForge preset, or null when the file is a verbatim source download.
    /// </summary>
    public string? Preset { get; init; }

    /// <summary>
    /// What the user calls this file when crateside adopted it (ADR 0022). Anything deciding what a
    /// re-cut may delete must read it, or it takes the file for the separation source.
    /// </summary>
    public string? AdoptedAs { get; init; }

    /// <summary>Whether it was downloaded as premium audio. Null when the file does not say.</summary>
    public bool? IsPremium { get; init; }

    /// <summary>crateside took this file in rather than making it.</summary>
    public bool IsAdopted => AdoptedAs is not null;

    /// <summary>
    /// A stem made to be <em>looked at</em> rather than heard: the drums, deleted once warping
    /// is done. Every drums stem is one, with no way to say otherwise.
    /// </summary>
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
