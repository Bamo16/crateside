using System.Text.Json;
using Crateside.Core.Familiarity;

namespace Crateside.Core.Genres;

/// <summary>
/// Asks free sources what genre a song is: MusicBrainz's community tags for the recording and then
/// the artist, Deezer's album genres, then iTunes's one storefront genre (#4). Needs no key.
/// </summary>
public sealed class GenreLookup(IHttpClientFactory clients)
{
    public const string ClientName = "genre-lookup";

    /// <summary>MusicBrainz allows one request a second from a client like this.</summary>
    private static readonly TimeSpan MusicBrainzPace = TimeSpan.FromSeconds(1.1);

    private static readonly TimeSpan DeezerPace = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Everything the sources said, recording first, then the artist, Deezer and iTunes, or null
    /// when a source could not be reached — which is worth asking again, where an empty answer is not.
    /// </summary>
    public async Task<GenreHearing?> HearAsync(
        string artist,
        string title,
        CancellationToken ct = default
    )
    {
        try
        {
            var (recording, byArtist, mbid) = await MusicBrainzAsync(artist, title, ct);
            var deezer = await DeezerAsync(artist, title, ct);
            var storefront = await ITunesAsync(artist, title, ct);

            return new GenreHearing(
                [
                    .. recording
                        .Concat(byArtist)
                        .Concat(deezer)
                        .Concat(storefront is null ? [] : [storefront])
                        .Distinct(StringComparer.OrdinalIgnoreCase),
                ],
                mbid
            );
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException
                && !ct.IsCancellationRequested
            )
        {
            return null;
        }
    }

    private async Task<(
        IReadOnlyList<string> Recording,
        IReadOnlyList<string> Artist,
        string? Mbid
    )> MusicBrainzAsync(string artist, string title, CancellationToken ct)
    {
        var query = Uri.EscapeDataString(
            $"recording:\"{Quoted(title)}\" AND artist:\"{Quoted(artist)}\""
        );

        using var search = await GetAsync(
            $"https://musicbrainz.org/ws/2/recording?query={query}&limit=1&fmt=json",
            ct
        );

        if (
            !search.RootElement.TryGetProperty("recordings", out var recordings)
            || recordings.GetArrayLength() is 0
        )
            return ([], [], null);

        var hit = recordings[0];
        var mbid = hit.GetProperty("id").GetString();

        await Task.Delay(MusicBrainzPace, ct);

        using var recording = await GetAsync(
            $"https://musicbrainz.org/ws/2/recording/{mbid}?inc=genres+artist-credits&fmt=json",
            ct
        );

        IReadOnlyList<string> byArtist = [];

        if (
            recording.RootElement.TryGetProperty("artist-credit", out var credits)
            && credits.GetArrayLength() > 0
        )
        {
            await Task.Delay(MusicBrainzPace, ct);

            using var credited = await GetAsync(
                $"https://musicbrainz.org/ws/2/artist/{credits[0].GetProperty("artist").GetProperty("id").GetString()}?inc=genres&fmt=json",
                ct
            );

            byArtist = Genres(credited.RootElement, take: 5);
        }

        await Task.Delay(MusicBrainzPace, ct);

        return (Genres(recording.RootElement, take: 5), byArtist, mbid);
    }

    /// <summary>The genres of the album Deezer's best match for the song is on, or none when it matches nothing.</summary>
    private async Task<IReadOnlyList<string>> DeezerAsync(
        string artist,
        string title,
        CancellationToken ct
    )
    {
        using var search = await GetAsync(FamiliarityLookup.SearchUrl(artist, title), ct);

        if (
            FamiliarityLookup.Match(search.RootElement, artist, title) is not { AlbumId: { } album }
        )
            return [];

        await Task.Delay(DeezerPace, ct);

        using var detail = await GetAsync($"https://api.deezer.com/album/{album}", ct);

        return
            detail.RootElement.TryGetProperty("genres", out var genres)
            && genres.TryGetProperty("data", out var data)
            ?
            [
                .. data.EnumerateArray()
                    .Select(genre => genre.GetProperty("name").GetString())
                    .OfType<string>(),
            ]
            : [];
    }

    /// <summary>The storefront's genre, when its first hit is by the same artist; else null.</summary>
    private async Task<string?> ITunesAsync(string artist, string title, CancellationToken ct)
    {
        using var found = await GetAsync(
            $"https://itunes.apple.com/search?term={Uri.EscapeDataString($"{artist} {title}")}&entity=song&limit=1",
            ct
        );

        return
            found.RootElement.TryGetProperty("results", out var results)
            && results.GetArrayLength() > 0
            && results[0] is var hit
            && hit.TryGetProperty("artistName", out var name)
            && SameArtist(artist, name.GetString())
            && hit.TryGetProperty("primaryGenreName", out var genre)
            ? genre.GetString()
            : null;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        using var response = await clients.CreateClient(ClientName).GetAsync(url, ct);

        response.EnsureSuccessStatusCode();

        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
    }

    /// <summary>A source's genres, most-voted first.</summary>
    private static IReadOnlyList<string> Genres(JsonElement entity, int take) =>
        entity.TryGetProperty("genres", out var genres)
            ?
            [
                .. genres
                    .EnumerateArray()
                    .OrderByDescending(g =>
                        g.TryGetProperty("count", out var count) ? count.GetInt32() : 0
                    )
                    .Select(g => g.GetProperty("name").GetString())
                    .OfType<string>()
                    .Take(take),
            ]
            : [];

    /// <summary>A hit counts only when it shares a word with the artist asked for, so a cover lends nothing.</summary>
    private static bool SameArtist(string asked, string? found) =>
        found is not null && Words(asked).Overlaps(Words(found));

    private static HashSet<string> Words(string text) =>
        text.ToLowerInvariant()
            .Split([' ', ',', '&', '-', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1 && word is not ("the" or "feat" or "ft" or "and"))
            .ToHashSet(StringComparer.Ordinal);

    private static string Quoted(string text) => text.Replace("\"", string.Empty);
}
