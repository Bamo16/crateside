namespace Crateside.Core.Staging;

/// <summary>
/// The separation provenance StemForge writes into a stem's <c>COMMENT</c> tag.
/// </summary>
/// <remarks>
/// Ported from <c>StemForge.Core.Separation.AudioTagger</c>, copied rather than referenced.
/// StemForge remains the writer — if its format changes, this must follow by hand.
/// </remarks>
public sealed record StemProvenance
{
    /// <summary>The <c>stemforge/&lt;version&gt;</c> that produced the stem.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>The separation preset, e.g. <c>Vocal - Full</c>. Absent on older stems.</summary>
    public string? Preset { get; init; }

    /// <summary>The separation date as written (<c>yyyy-MM-dd</c>, UTC).</summary>
    public DateOnly? SeparatedOn { get; init; }

    /// <summary>
    /// The source URL the track was downloaded from.
    /// </summary>
    /// <remarks>
    /// Only present when the local file is one StemForge downloaded. A file acquired any other
    /// way has no <c>stemforge/</c> comment, so it yields no URL and no
    /// <see cref="SourceVideoId"/>.
    /// </remarks>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The YouTube video ID extracted from <see cref="SourceUrl"/> — the join key onto the
    /// keytag ledger, and the reason this parse exists.
    /// </summary>
    public string? SourceVideoId { get; init; }

    /// <summary>
    /// Parses a StemForge provenance comment. Returns null for anything not carrying the
    /// <c>stemforge/</c> prefix, rather than attempt to parse it as garbage.
    /// </summary>
    public static StemProvenance? Parse(string? comment)
    {
        if (comment is null || !comment.StartsWith("stemforge/", StringComparison.Ordinal))
            return null;

        string? preset = null;
        DateOnly? separatedOn = null;
        string? sourceUrl = null;

        // BuildProvenance joins with " | "; the first part is always the tool version.
        var parts = comment.Split(" | ");

        foreach (var part in parts.AsSpan(1))
        {
            if (Value(part, "preset: ") is { } p)
                preset = p;
            else if (Value(part, "date: ") is { } d && DateOnly.TryParse(d, out var parsed))
                separatedOn = parsed;
            else if (Value(part, "source: ") is { } s)
                sourceUrl = s;
        }

        return new StemProvenance
        {
            ToolVersion = parts[0],
            Preset = preset,
            SeparatedOn = separatedOn,
            SourceUrl = sourceUrl,
            SourceVideoId = ExtractYouTubeVideoId(sourceUrl),
        };
    }

    private static string? Value(string part, string prefix) =>
        part.StartsWith(prefix, StringComparison.Ordinal)
        && part[prefix.Length..] is { Length: > 0 } value
            ? value
            : null;

    /// <summary>The video ID a source URL names, or null when it is not a YouTube one.</summary>
    public static string? ExtractYouTubeVideoId(string? url) => YouTubeUrl.VideoIdOf(url);
}
