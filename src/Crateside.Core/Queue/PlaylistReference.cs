using System.Text.RegularExpressions;

namespace Crateside.Core.Queue;

/// <summary>
/// Parsing and sanity-checking of a playlist ID, which the user supplies by hand.
/// </summary>
public static partial class PlaylistReference
{
    /// <summary>
    /// Accepts a bare playlist ID or any YouTube URL carrying one, and returns the ID.
    /// Pasting the URL straight from the address bar is what people actually have in hand.
    /// </summary>
    public static string? Parse(string? input)
    {
        if (input?.Trim() is not { Length: > 0 } trimmed)
            return null;

        if (
            Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && QueryValue(uri.Query, "list") is { Length: > 0 } fromQuery
        )
        {
            return IsWellFormed(fromQuery) ? fromQuery : null;
        }

        return IsWellFormed(trimmed) ? trimmed : null;
    }

    /// <summary>
    /// Whether the ID looks like a playlist ID at all. Deliberately loose — YouTube's
    /// prefixes are not documented as a closed set, so this rejects obvious nonsense rather
    /// than trying to be authoritative.
    /// </summary>
    public static bool IsWellFormed(string? id) =>
        id is { Length: >= 2 } && PlaylistIdPattern().IsMatch(id);

    /// <summary>
    /// Watch Later cannot be read through the API at all, with any credential — Google
    /// removed it. Worth saying plainly rather than letting it fail as "not found".
    /// </summary>
    public static bool IsWatchLater(string? id) =>
        string.Equals(id?.Trim(), "WL", StringComparison.OrdinalIgnoreCase);

    /// <summary>An explanation of why an ID will not work, or null if it looks usable.</summary>
    public static string? Problem(string? id) =>
        id switch
        {
            null or "" => "Enter a playlist ID or paste the playlist URL.",
            _ when IsWatchLater(id) =>
                "Watch Later cannot be read through the YouTube API by any credential. "
                    + "The Queue has to be an ordinary playlist.",
            _ when !IsWellFormed(id) =>
                "That does not look like a playlist ID. Paste the playlist URL, or the "
                    + "'list=' value from it.",
            _ => null,
        };

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            if (pair.Split('=') is [var name, var value] && name == key)
                return Uri.UnescapeDataString(value);
        }

        return null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{2,64}$")]
    private static partial Regex PlaylistIdPattern();
}
