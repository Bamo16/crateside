using System.Text.Json;
using System.Text.RegularExpressions;
using Crateside.Core.Tracks;

namespace Crateside.Core.Genres;

/// <summary>Picks the hit in Deezer's public search that is the song itself, for the genres of its album.</summary>
internal static partial class DeezerMatch
{
    [GeneratedRegex(
        @"karaoke|tribute|originally\s+performed|made\s+famous|in\s+the\s+style\s+of|backing\s+track|\bcovered\s+by\b|\bcover\s+(version|band)\b|[\(\[][^\)\]]*\bcover\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ImitationMarker { get; }

    public static string SearchUrl(string artist, string title) =>
        $"https://api.deezer.com/search?q={Uri.EscapeDataString($"{ArtistCredit.Primary(artist)} {NameMatch.WithoutBrackets(title)}")}";

    /// <summary>
    /// The most popular hit that is this song by one of its artists, so the original outranks a
    /// live cut or a remaster of it. A hit by a karaoke, tribute or cover act is passed over for the
    /// next one.
    /// </summary>
    public static (long Id, int Rank, long? AlbumId)? Match(
        JsonElement search,
        string artist,
        string title
    ) =>
        search.TryGetProperty("data", out var hits)
            ? hits.EnumerateArray()
                .Where(hit =>
                    hit.TryGetProperty("title", out var found)
                    && SameTitle(found.GetString() ?? string.Empty, title)
                    && hit.GetProperty("artist").GetProperty("name").GetString() is { } by
                    && NameMatch.ByOneOf(artist, by)
                    && !IsImitation(found.GetString() ?? string.Empty, by, title)
                )
                .Select(hit =>
                    ((long Id, int Rank, long? AlbumId)?)
                        (
                            hit.GetProperty("id").GetInt64(),
                            hit.GetProperty("rank").GetInt32(),
                            hit.TryGetProperty("album", out var album)
                            && album.TryGetProperty("id", out var albumId)
                                ? albumId.GetInt64()
                                : null
                        )
                )
                .MaxBy(hit => hit!.Value.Rank)
            : null;

    /// <summary>
    /// The same title once both are squashed to letters and digits, one a prefix of the other, and
    /// with Deezer's censoring asterisks standing for any letter.
    /// </summary>
    public static bool SameTitle(string found, string wanted)
    {
        var (f, w) = (NameMatch.Squash(found), NameMatch.Squash(wanted));

        if (f.Length is 0 || w.Length is 0)
            return false;

        if (!f.Contains('*'))
            return f.StartsWith(w, StringComparison.Ordinal)
                || w.StartsWith(f, StringComparison.Ordinal);

        return w.Length >= f.Length
            && f.Zip(w).All(pair => pair.First is '*' || pair.First == pair.Second);
    }

    /// <summary>Whether the hit calls itself a karaoke, tribute or cover rendition the wanted song is not.</summary>
    public static bool IsImitation(string foundTitle, string foundArtist, string wantedTitle) =>
        ImitationMarker.Match($"{foundTitle} | {foundArtist}")
            is { Success: true, Value: var marker }
        && !wantedTitle.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
