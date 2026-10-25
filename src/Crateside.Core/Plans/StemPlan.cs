using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Plans;

/// <summary>
/// Which files the user wants made for one track: the separator presets to run, and whether the
/// source track is itself a keeper (ADR 0018).
/// </summary>
public sealed record StemPlan
{
    /// <summary>What a track nobody has chosen for gets: the usual pair.</summary>
    public static StemPlan Default { get; } =
        new() { Presets = ["vocal_full", "instrumental_full"] };

    /// <summary>
    /// No vocal to separate out, so the source track is the sample; what
    /// <c>instrumentalRelease: true</c> converts to.
    /// </summary>
    public static StemPlan InstrumentalRelease { get; } = new() { Presets = [], KeepSource = true };

    /// <summary>
    /// The separator presets to run, one file each. crateside's own names, not the separator's
    /// display names — <c>vocal_full</c>, never <c>Vocal - Full</c>.
    /// </summary>
    public required IReadOnlyList<string> Presets { get; init; }

    /// <summary>Whether the separation source is itself one of the track's samples.</summary>
    public bool KeepSource { get; init; }

    /// <summary>Whole files of this song the user attached, which nothing separates (ADR 0019).</summary>
    public IReadOnlyList<AddedSource> AddedSources { get; init; } = [];

    /// <summary>Files of this song the user made and gave to crateside (ADR 0022).</summary>
    public IReadOnlyList<AdoptedSample> AdoptedSamples { get; init; } = [];

    /// <summary>
    /// Whether this plan asks for nothing at all, which no track should be left in.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => this is { Presets.Count: 0, KeepSource: false, AdoptedSamples.Count: 0 };

    /// <summary>The same plan with one preset ticked or unticked.</summary>
    public StemPlan Toggling(string presetId) =>
        this with
        {
            Presets = Presets.Contains(presetId, StringComparer.OrdinalIgnoreCase)
                ?
                [
                    .. Presets.Where(preset =>
                        !preset.Equals(presetId, StringComparison.OrdinalIgnoreCase)
                    ),
                ]
                : [.. Presets, presetId],
        };

    /// <summary>The same plan with the separation source kept, or not.</summary>
    public StemPlan TogglingSource() => this with { KeepSource = !KeepSource };

    /// <summary>The same plan with one more source attached.</summary>
    public StemPlan Adding(AddedSource source) =>
        this with
        {
            AddedSources = [.. AddedSources, source],
        };

    /// <summary>The same plan without the added source that carries this label.</summary>
    public StemPlan Dropping(string label) =>
        this with
        {
            AddedSources =
            [
                .. AddedSources.Where(source =>
                    !source.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
                ),
            ],
        };

    /// <summary>The same plan with one more file of the user's own kept.</summary>
    public StemPlan Keeping(AdoptedSample adopted) =>
        this with
        {
            AdoptedSamples = [.. AdoptedSamples, adopted],
        };

    /// <summary>
    /// The same plan without the adopted sample that carries this label, which a re-cut then
    /// deletes for good.
    /// </summary>
    public StemPlan Forgetting(string label) =>
        this with
        {
            AdoptedSamples =
            [
                .. AdoptedSamples.Where(adopted =>
                    !adopted.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
                ),
            ],
        };

    /// <summary>
    /// Whether two plans ask for the same files, whatever order they were built in — which the
    /// record's own equality does not ignore.
    /// </summary>
    public bool AsksTheSameAs(StemPlan other) =>
        KeepSource == other.KeepSource
        && Presets.Count == other.Presets.Count
        && !Presets.Except(other.Presets, StringComparer.Ordinal).Any()
        && AddedSources.Count == other.AddedSources.Count
        && !AddedSources
            .Select(source => source.VideoId)
            .Except(other.AddedSources.Select(source => source.VideoId), StringComparer.Ordinal)
            .Any()
        && AdoptedSamples.Count == other.AdoptedSamples.Count
        && !AdoptedSamples
            .Select(adopted => adopted.Label)
            .Except(
                other.AdoptedSamples.Select(adopted => adopted.Label),
                StringComparer.OrdinalIgnoreCase
            )
            .Any();
}

/// <summary>
/// A file of this song the user made themselves, kept as a sample and never separated. It came
/// from no upload, so its label is its identity (ADR 0022).
/// </summary>
public sealed record AdoptedSample
{
    /// <summary>What the user calls it, which is also the file's parenthesized suffix.</summary>
    public required string Label { get; init; }
}

/// <summary>A whole file of this song the user attached: downloaded, staged, tagged, never cut.</summary>
public sealed record AddedSource
{
    public required string VideoId { get; init; }

    /// <summary>
    /// What the user called it, which becomes the file's parenthesized suffix — unique per track,
    /// since it tells two added sources apart on disk (ADR 0019).
    /// </summary>
    public required string Label { get; init; }

    /// <summary>Null only on a source added before the user was asked (ADR 0028).</summary>
    public SampleType? Type { get; init; }

    [JsonIgnore]
    public string WatchUrl => $"https://music.youtube.com/watch?v={VideoId}";
}
