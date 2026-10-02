using Crateside.Core.Pairing;
using Crateside.Core.Tempo;

namespace Crateside.Core.Library;

/// <summary>Artists with <see cref="Minimum"/> or more songs, by first credited name (ADR 0033).</summary>
public sealed record ArtistCounts(IReadOnlyList<ArtistCount> Artists, int AllArtists)
{
    public const int Minimum = 3;

    private const int KeysShown = 3;

    public int Largest => Artists.Count is 0 ? 0 : Artists.Max(artist => artist.Songs);

    public static ArtistCounts From(IEnumerable<LibrarySong> songs)
    {
        var byArtist = songs
            .Where(song => song.Artist is not null)
            .GroupBy(song => song.Artist!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ArtistCounts(
            [
                .. byArtist
                    .Where(group => group.Count() >= Minimum)
                    .Select(Count)
                    .OrderByDescending(artist => artist.Songs)
                    .ThenBy(artist => artist.Artist, StringComparer.OrdinalIgnoreCase),
            ],
            byArtist.Count
        );
    }

    private static ArtistCount Count(IGrouping<string, LibrarySong> songs)
    {
        return new ArtistCount(
            songs.GroupBy(song => song.Artist!).MaxBy(spelling => spelling.Count())!.Key,
            songs.Count(),
            [
                .. songs
                    .Select(song => song.Key)
                    .OfType<MusicKey>()
                    .GroupBy(key => key)
                    .OrderByDescending(key => key.Count())
                    .ThenBy(key => key.Key)
                    .Take(KeysShown)
                    .Select(key => key.Key.CamelotCode),
            ],
            TempoFormat.Range(songs.Select(song => song.Bpm).OfType<TrackTempo>())
        );
    }
}

/// <param name="BpmRange">Slowest to fastest headline tempo, rounded outward; null when none has one.</param>
public sealed record ArtistCount(
    string Artist,
    int Songs,
    IReadOnlyList<string> Keys,
    string? BpmRange
);
