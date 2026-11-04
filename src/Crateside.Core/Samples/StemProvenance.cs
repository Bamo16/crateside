using System.Text.Json.Serialization;

namespace Crateside.Core.Samples;

/// <summary>
/// The provenance comment in a sample's <c>COMMENT</c> tag: StemForge's format, followed by hand,
/// or crateside's own for an adopted file (ADR 0022).
/// </summary>
public sealed record StemProvenance
{
    /// <summary>What crateside writes in front of a comment it wrote itself.</summary>
    public const string AdoptedPrefix = "crateside/";

    private const string SeparatedPrefix = "stemforge/";

    /// <summary>The part crateside appends to say what a file never cut is (ADR 0028).</summary>
    public const string TypePrefix = "type: ";

    /// <summary>The <c>stemforge/&lt;version&gt;</c> or <c>crateside/&lt;version&gt;</c> that wrote it.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>What the user calls an adopted sample, which is also its filename suffix.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// crateside wrote this comment and the user named the file, so it is one it adopted rather
    /// than made (ADR 0022). A copy of a track's own source, stamped with no label, is not.
    /// </summary>
    public bool IsAdopted =>
        Label is not null && ToolVersion.StartsWith(AdoptedPrefix, StringComparison.Ordinal);

    /// <summary>The separation preset, e.g. <c>Vocal - Full</c>. Absent on older stems.</summary>
    public string? Preset { get; init; }

    /// <summary>The models that cut it, as an early stem names them instead of a preset; null on a download.</summary>
    public string? Model { get; init; }

    /// <summary>The YouTube format StemForge downloaded, e.g. <c>141</c>. Absent before 2026-06-01.</summary>
    public string? FormatId { get; init; }

    /// <summary>
    /// Whether the download was premium audio, by StemForge's rule. Null when no format was recorded.
    /// </summary>
    public bool? IsPremium => FormatId is null ? null : FormatId is "141" or "774";

    /// <summary>What the user said a file never cut is. Null on every stem, whose preset says it.</summary>
    public SampleType? Type { get; init; }

    /// <summary>The separation date as written (<c>yyyy-MM-dd</c>, UTC).</summary>
    public DateOnly? SeparatedOn { get; init; }

    /// <summary>
    /// The source URL the track was downloaded from — present only when StemForge downloaded it.
    /// </summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The work key read from <see cref="SourceUrl"/> — the join key onto the
    /// key ledger, and the reason this parse exists.
    /// </summary>
    [JsonPropertyName("sourceVideoId")]
    public string? SourceTrackKey { get; init; }

    /// <summary>
    /// Parses a provenance comment. Returns null for anything carrying neither known prefix,
    /// rather than attempt to parse it as garbage.
    /// </summary>
    public static StemProvenance? Parse(string? comment)
    {
        if (
            comment is null
            || !(
                comment.StartsWith(SeparatedPrefix, StringComparison.Ordinal)
                || comment.StartsWith(AdoptedPrefix, StringComparison.Ordinal)
            )
        )
            return null;

        string? preset = null;
        string? label = null;
        DateOnly? separatedOn = null;
        string? sourceUrl = null;
        SampleType? type = null;
        string? formatId = null;
        string? model = null;

        // BuildProvenance joins with " | "; the first part is always the tool version.
        var parts = comment.Split(" | ");

        foreach (var part in parts.AsSpan(1))
        {
            if (Value(part, "preset: ") is { } p)
                preset = p;
            else if (Value(part, "label: ") is { } l)
                label = l;
            else if (Value(part, "date: ") is { } d && DateOnly.TryParse(d, out var parsed))
                separatedOn = parsed;
            else if (Value(part, "model: ") is { } m)
                model = m;
            else if (Value(part, "source: ") is { } s)
                sourceUrl = s;
            else if (Value(part, "format-id: ") is { } f)
                formatId = f;
            else if (
                Value(part, TypePrefix) is { } t
                && Enum.TryParse<SampleType>(t, ignoreCase: false, out var parsedType)
            )
                type = parsedType;
        }

        return new StemProvenance
        {
            ToolVersion = parts[0],
            Preset = preset,
            Model = model,
            Label = label,
            SeparatedOn = separatedOn,
            SourceUrl = sourceUrl,
            Type = type,
            FormatId = formatId,
            SourceTrackKey = TrackKeys.TryParse(sourceUrl, out var trackKey) ? trackKey : null,
        };
    }

    private static string? Value(string part, string prefix) =>
        part.StartsWith(prefix, StringComparison.Ordinal)
        && part[prefix.Length..] is { Length: > 0 } value
            ? value
            : null;
}
