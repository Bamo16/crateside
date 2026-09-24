namespace Crateside.Core.Staging;

/// <summary>
/// The provenance comment carried in a sample's <c>COMMENT</c> tag.
/// </summary>
/// <remarks>
/// StemForge writes most of these and owns its own format — if that changes, this must follow by
/// hand. crateside writes the <c>crateside/</c> ones itself, for a file it adopted (ADR 0022).
/// </remarks>
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
    /// crateside wrote this comment, so the file is one it adopted rather than made (ADR 0022).
    /// </summary>
    public bool IsAdopted => ToolVersion.StartsWith(AdoptedPrefix, StringComparison.Ordinal);

    /// <summary>The separation preset, e.g. <c>Vocal - Full</c>. Absent on older stems.</summary>
    public string? Preset { get; init; }

    /// <summary>What the user said a file never cut is. Null on every stem, whose preset says it.</summary>
    public SampleType? Type { get; init; }

    /// <summary>The separation date as written (<c>yyyy-MM-dd</c>, UTC).</summary>
    public DateOnly? SeparatedOn { get; init; }

    /// <summary>
    /// The source URL the track was downloaded from.
    /// </summary>
    /// <remarks>
    /// Only present when StemForge downloaded the file: any other way yields no
    /// <c>stemforge/</c> comment, no URL and no <see cref="SourceVideoId"/>.
    /// </remarks>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The YouTube video ID extracted from <see cref="SourceUrl"/> — the join key onto the
    /// keytag ledger, and the reason this parse exists.
    /// </summary>
    public string? SourceVideoId { get; init; }

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
            else if (Value(part, "source: ") is { } s)
                sourceUrl = s;
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
            Label = label,
            SeparatedOn = separatedOn,
            SourceUrl = sourceUrl,
            Type = type,
            SourceVideoId = YouTubeUrl.TryGetVideoId(sourceUrl, out var videoId) ? videoId : null,
        };
    }

    private static string? Value(string part, string prefix) =>
        part.StartsWith(prefix, StringComparison.Ordinal)
        && part[prefix.Length..] is { Length: > 0 } value
            ? value
            : null;
}
