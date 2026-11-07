using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Web;

namespace Crateside.Core.Playlist;

/// <summary>
/// Parsing and sanity-checking of a playlist ID, which the user supplies by hand.
/// </summary>
public static partial class PlaylistReference
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{2,64}$")]
    private static partial Regex PlaylistIdPattern { get; }

    /// <summary>
    /// Takes a bare playlist ID or any YouTube URL carrying one.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out string? playlistId)
    {
        playlistId = input?.Trim() switch
        {
            not { Length: > 0 } => null,

            // GetValues, not the indexer — a repeated list= would join as "A,B" via the
            // indexer and fail the shape check.
            { } trimmed when Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) => HttpUtility
                .ParseQueryString(uri.Query)
                .GetValues("list")
                ?.FirstOrDefault(IsWellFormed),

            { } trimmed => IsWellFormed(trimmed) ? trimmed : null,
        };

        return playlistId is not null;
    }

    /// <summary>
    /// An explanation of why some input will not work as a playlist, or null if it is usable.
    /// </summary>
    public static string? Problem(string? input) =>
        input?.Trim() switch
        {
            not { Length: > 0 } => "Enter a playlist ID or paste the playlist URL.",

            // WL is well-formed but not readable via the API; without this it fails later
            // as a bare "not found".
            { } trimmed when IsWatchLater(trimmed) =>
                "Watch Later cannot be read through the YouTube API by any credential. "
                    + "The Queue has to be an ordinary playlist.",

            { } trimmed when !TryParse(trimmed, out _) =>
                "That does not look like a playlist ID. Paste the playlist URL, or the "
                    + "'list=' value from it.",

            _ => null,
        };

    /// <summary>
    /// Deliberately loose — rejects obvious nonsense rather than being authoritative.
    /// </summary>
    private static bool IsWellFormed(string id) => PlaylistIdPattern.IsMatch(id);

    private static bool IsWatchLater(string id) =>
        string.Equals(id, "WL", StringComparison.OrdinalIgnoreCase);
}
