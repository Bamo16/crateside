namespace Crateside.Core.Staging;

/// <summary>
/// The separation provenance StemForge writes into a stem's <c>COMMENT</c> tag.
/// </summary>
/// <remarks>
/// Ported from <c>StemForge.Core.Separation.AudioTagger</c> (<c>BuildProvenance</c> /
/// <c>ParseProvenance</c>). Copied rather than referenced: <c>StemForge.Core</c> targets
/// net11.0, which crateside must not, and it carries UI and separator dependencies
/// irrelevant here. StemForge remains the writer; if its format changes, this follows.
/// </remarks>
public sealed record StemProvenance
{
    /// <summary>The <c>stemforge/&lt;version&gt;</c> that produced the stem.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>The separation preset, e.g. <c>Vocal - Full</c>. Absent on older stems.</summary>
    public string? Preset { get; init; }

    /// <summary>The separation date as written (<c>yyyy-MM-dd</c>, UTC).</summary>
    public DateOnly? SeparatedOn { get; init; }

    /// <summary>The source URL the track was downloaded from. Absent for local-file jobs.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The YouTube video ID extracted from <see cref="SourceUrl"/> — the join key onto the
    /// keytag ledger, and the reason this parse exists.
    /// </summary>
    public string? SourceVideoId { get; init; }

    /// <summary>
    /// Parses a StemForge provenance comment. Returns null for anything this tool did not
    /// write — the <c>stemforge/</c> prefix is the gate, and keeping it is what makes an
    /// untagged or foreign file read as "no video ID" rather than as garbage.
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

    /// <summary>
    /// Pulls the 11-character video ID out of a YouTube URL, covering the
    /// <c>watch?v=</c>, <c>youtu.be/</c> and <c>/shorts/</c> spellings. Returns null for a
    /// non-YouTube or unparseable URL rather than guessing.
    /// </summary>
    public static string? ExtractYouTubeVideoId(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.TrimStart("www.".ToCharArray());

        string? candidate = null;

        if (host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            candidate = uri.AbsolutePath.Trim('/');
        }
        else if (host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');

            candidate = segments switch
            {
                ["shorts" or "embed" or "v", var id, ..] => id,
                _ => QueryValue(uri.Query, "v"),
            };
        }

        return IsVideoId(candidate) ? candidate : null;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            if (pair.Split('=') is [var name, var value] && name == key)
                return Uri.UnescapeDataString(value);
        }

        return null;
    }

    // YouTube IDs are exactly 11 characters of base64url. Checking the shape keeps a
    // malformed URL from producing a plausible-looking key that silently joins to nothing.
    private static bool IsVideoId(string? value) =>
        value is { Length: 11 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
