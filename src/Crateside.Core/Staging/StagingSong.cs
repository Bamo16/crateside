using Crateside.Core.Ledger;

namespace Crateside.Core.Staging;

/// <summary>
/// One row of the staging board: a song, with its stems rolled up.
/// </summary>
/// <remarks>
/// The row is the song because the key is a property of the song, while completion is per
/// stem and ragged. Entirely derived — nothing here is stored, and nothing is marked done by
/// hand.
/// </remarks>
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

    public int TaggedStemCount => Stems.Count(s => s.IsKeyTagged);

    /// <summary>
    /// True when every stem carries a key. With no reliable warp signal, this is the only
    /// per-stem progress the board can honestly report.
    /// </summary>
    public bool IsFullyTagged => Stems.Count > 0 && TaggedStemCount == Stems.Count;

    /// <summary>
    /// Stems tagged in Ableton with a key that is not the ledger's current verdict — which is
    /// what a supersede leaves behind. Worth surfacing: the tag is what pairing actually uses,
    /// so a stale one silently misroutes mashups.
    /// </summary>
    public IReadOnlyList<StagingStem> StaleTaggedStems =>
        [.. Stems.Where(s => s.KeyDisagreesWithVerdict)];
}

/// <summary>One separated stem of a staged song.</summary>
public sealed record StagingStem
{
    public required string Path { get; init; }
    public required string FileName { get; init; }

    /// <summary>The StemForge preset — Vocal - Full, Instrumental - Full, Drums.</summary>
    public string? Preset { get; init; }

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
    public string Label => Preset ?? System.IO.Path.GetFileNameWithoutExtension(FileName);
}
