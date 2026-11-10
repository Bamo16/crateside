using Crateside.Core.Pairing;
using Crateside.Core.Tracks;

namespace Crateside.Core.Library;

/// <summary>The songs whose stems are in Staging, shaped like the library's so a picker can list both (#155).</summary>
public static class StagedSongs
{
    /// <summary>
    /// A song for each row whose stems have been handed off to Staging and which the library does
    /// not hold; a track still downloading, separating or being handed off is left out.
    /// </summary>
    public static IReadOnlyList<LibrarySong> Of(
        IEnumerable<TrackRow> rows,
        IEnumerable<LibrarySong> library
    )
    {
        var held = library.Select(song => song.Song).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. rows.Where(row => row.State is TrackState.ReadyToWarp or TrackState.ReadyToMove)
                .Where(row => !held.Contains(row.Song))
                .Select(From)
                .OrderBy(song => song.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static LibrarySong From(TrackRow row) =>
        new()
        {
            Song = row.Song,
            Name = row.Title,
            Artist = row.Title.Split(" - ", 2) is [{ Length: > 0 } credited, _]
                ? ArtistCredit.Primary(credited)
                : null,
            Key = MusicKey.FromString(row.Key),
            Genres = row.Genre?.Genres ?? [],
            Tempo = row.Tempo,
            Beats = row.Beats,
            Starter = StarterFates.Of(row.Beats, row.Tempo),
            Run = row.Verdict,
            IsStaged = true,
        };
}
