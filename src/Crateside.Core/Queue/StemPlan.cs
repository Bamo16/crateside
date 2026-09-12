using System.Text.Json.Serialization;

namespace Crateside.Core.Queue;

/// <summary>
/// Which files the user wants made for one track: the separator presets to run, and whether the
/// source track is itself a keeper.
/// </summary>
/// <remarks>
/// The plan is the record. crateside used to store a <em>fact</em> — the release has no vocals —
/// and derive the recipe from it, so the plan could change later without the record becoming a
/// lie (ADR 0004). That reasoning needed there to be one fact underneath; once a track can ask
/// for any of six presets and the source, there is none, and what the user asked for is the only
/// thing left to keep (ADR 0018).
/// </remarks>
public sealed record StemPlan
{
    /// <summary>What a track nobody has chosen for gets: the usual pair.</summary>
    public static StemPlan Default { get; } =
        new() { Presets = ["vocal_full", "instrumental_full"] };

    /// <summary>
    /// No vocal to separate out, so the source track is the sample.
    /// </summary>
    /// <remarks>
    /// What <c>instrumentalRelease: true</c> used to derive, and what it converts to. Not a mode
    /// the picker offers — it is reachable by unticking the vocal preset and ticking the source,
    /// which is the same thing said in the picker's own vocabulary.
    /// </remarks>
    public static StemPlan InstrumentalRelease { get; } = new() { Presets = [], KeepSource = true };

    /// <summary>
    /// The separator presets to run, one file each. crateside's own names, not the separator's
    /// display names — <c>vocal_full</c>, never <c>Vocal - Full</c>.
    /// </summary>
    [JsonPropertyName("presets")]
    public required IReadOnlyList<string> Presets { get; init; }

    /// <summary>Whether the downloaded source track is itself one of the track's samples.</summary>
    [JsonPropertyName("keepSource")]
    public bool KeepSource { get; init; }

    /// <summary>
    /// Whether this plan asks for nothing at all, which no track should be left in.
    /// </summary>
    public bool IsEmpty => Presets.Count is 0 && !KeepSource;

    /// <summary>Whether two plans ask for the same files, whatever order they were built in.</summary>
    /// <remarks>
    /// The record's own equality is sequence-sensitive on <see cref="Presets"/>, which would make
    /// a reordered list read as a change and offer a re-cut that would make nothing new.
    /// </remarks>
    public bool AsksTheSameAs(StemPlan other) =>
        KeepSource == other.KeepSource
        && Presets.Count == other.Presets.Count
        && !Presets.Except(other.Presets, StringComparer.Ordinal).Any();
}
