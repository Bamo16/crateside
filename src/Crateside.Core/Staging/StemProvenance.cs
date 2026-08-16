using System.Text.RegularExpressions;

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
public sealed partial record StemProvenance
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
    /// <strong>A local-file job keeps this, provided the local file is one StemForge
    /// downloaded.</strong> <c>AudioTagger.ReadSourceTagInfo</c> parses the input's own
    /// <c>COMMENT</c> and carries the URL forward into the stems it writes — its doc names the
    /// case, "download once, then separate on the saved path", which is exactly what crateside's
    /// working set does. A file acquired any other way has no <c>stemforge/</c> comment, so it
    /// yields no URL and therefore no <see cref="SourceVideoId"/>, and every stem made from it
    /// joins to nothing. That is why the evidence run may not pick its own downloader.
    /// </remarks>
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
    /// Pulls the 11-character video ID out of a YouTube URL — or out of a bare ID — covering
    /// the <c>watch?v=</c>, <c>youtu.be/</c>, <c>/shorts/</c> and <c>/embed/</c> spellings,
    /// with or without a scheme and with any trailing query. Returns null for a non-YouTube
    /// or unparseable input rather than guessing.
    /// </summary>
    /// <remarks>
    /// The pattern is StemForge's, from <c>StemForge.Core.Downloading.YtUrlHelper</c> — the
    /// same expression that recognised these URLs on the way in now recognises them on the
    /// way out, so the two tools cannot disagree about what a video ID is. Only extraction is
    /// ported; StemForge's <c>TryNormalize</c> also canonicalises to a music.youtube.com URL
    /// and passes through non-YouTube URLs, neither of which crateside needs.
    /// </remarks>
    public static string? ExtractYouTubeVideoId(string? url) =>
        url?.Trim() is { Length: > 0 } trimmed
        && YtVideoIdRegex.Match(trimmed).Groups["VideoId"] is { Success: true, Value: var id }
            ? id
            : null;

    // The 11-character base64url shape is baked into the pattern, so a malformed URL yields
    // no match rather than a plausible-looking key that silently joins to nothing.
    [GeneratedRegex(
        @"^(?:(?:(?:https?:\/\/)?(?:(?:www|music|m)\.)?)?(?:youtube\.com|youtu\.be)(?:\S*?(?:\?v=|\/)))?(?<VideoId>[0-9A-Za-z_-]{11})(?:[&?].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    )]
    private static partial Regex YtVideoIdRegex { get; }
}
