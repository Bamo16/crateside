using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Crateside.Core;

/// <summary>
/// Reads and canonicalises YouTube references — the one place that knows the shape of a video
/// ID, copied from StemForge's <c>YtUrlHelper</c>.
/// </summary>
public static partial class YouTubeUrl
{
    /// <summary>
    /// A YouTube reference as a canonical watch URL, or any other http(s) URL unchanged. Anything
    /// handed to another tool goes through here.
    /// </summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        if (TryGetVideoId(input, out var id))
        {
            normalized = $"https://music.youtube.com/watch?v={id}";

            return true;
        }

        normalized =
            input?.Trim() is { Length: > 0 } trimmed
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
                ? trimmed
                : null;

        return normalized is not null;
    }

    /// <summary>
    /// The 11-character video ID out of a bare ID or a watch, youtu.be, shorts or embed URL —
    /// false for anything else, rather than a guess.
    /// </summary>
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
