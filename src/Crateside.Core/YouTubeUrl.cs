using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Crateside.Core;

/// <summary>
/// Reads and canonicalises YouTube references. The one place in crateside that knows the shape
/// of a video ID.
/// </summary>
/// <remarks>
/// The pattern is StemForge's, from <c>StemForge.Core.Downloading.YtUrlHelper</c>, so the two
/// tools cannot disagree about what a video ID is.
/// </remarks>
public static partial class YouTubeUrl
{
    /// <summary>
    /// A YouTube reference as a canonical watch URL, or any other http(s) URL unchanged.
    /// </summary>
    /// <remarks>
    /// Everything crateside hands to another tool goes through here: a bare video ID is a
    /// perfectly good thing to type and not a thing to pass on.
    /// </remarks>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        if (TryGetVideoId(input, out var id))
        {
            normalized = $"https://music.youtube.com/watch?v={id}";

            return true;
        }

        // Uri.TryCreate takes null and answers false, so nothing here has to guard it.
        normalized =
            Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
                ? uri.OriginalString
                : null;

        return normalized is not null;
    }

    /// <summary>
    /// The 11-character video ID out of a URL or a bare ID — <c>watch?v=</c>, <c>youtu.be/</c>,
    /// <c>/shorts/</c> and <c>/embed/</c>, with or without a scheme and with any trailing query.
    /// False for anything else, rather than a guess.
    /// </summary>
    /// <remarks><see cref="Regex.Match(string)"/> throws on null, so the guard is not defensive.</remarks>
    public static bool TryGetVideoId(string? input, [NotNullWhen(true)] out string? videoId)
    {
        videoId =
            input?.Trim() is { Length: > 0 } trimmed
            && VideoIdPattern.Match(trimmed).Groups.TryGetValue("VideoId", out var group)
            && group is { Success: true, Value: { } id }
                ? id
                : null;

        return videoId is not null;
    }

    [GeneratedRegex(
        @"^(?:(?:(?:https?:\/\/)?(?:(?:www|music|m)\.)?)?(?:youtube\.com|youtu\.be)(?:\S*?(?:\?v=|\/)))?(?<VideoId>[0-9A-Za-z_-]{11})(?:[&?].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    )]
    private static partial Regex VideoIdPattern { get; }
}
