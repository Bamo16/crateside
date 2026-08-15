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
    /// <remarks>
    /// Whether the <em>absence</em> of a warp guide positively means "warped" is a different and
    /// unproven claim; see <c>docs/adr/0004</c>. This says only that a warp guide is still here.
    /// </remarks>
    public IReadOnlyList<StagingStem> WarpGuides => [.. Stems.Where(s => s.IsWarpGuide)];

    public int TaggedStemCount => Samples.Count(s => s.IsKeyTagged);

    /// <summary>
    /// True when every <em>sample</em> carries a key — warp guides are excluded, because they are
    /// never tagged and are meant to be deleted.
    /// </summary>
    /// <remarks>
    /// Counting every file instead made a warp guide a permanent untagged row, clearing only when
    /// the user deleted it. That read as "warping in progress", which is nearly right and
    /// indistinguishable from "not tagged yet" — so the board could not say which step was
    /// actually outstanding. Now it can say both, separately.
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
    /// Every drums stem is treated as a warp guide, with no way to say otherwise. A drum stem is
    /// perfectly mixable and could be kept; that is rare enough that the user does it by hand,
    /// outside crateside. See <c>docs/adr/0004</c>.
    /// </remarks>
    public bool IsWarpGuide => Preset?.Contains("drum", StringComparison.OrdinalIgnoreCase) is true;

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
