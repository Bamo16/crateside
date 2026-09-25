using Crateside.Core.Genres;
using Crateside.Core.Tracks;

namespace Crateside;

/// <summary>
/// Asks the free sources for a genre suggestion for every song that has none, one at a time.
/// Never paused: it contends for nothing, and it writes no tag (ADR 0030).
/// </summary>
public sealed class GenreWorker(
    TracksService tracks,
    GenreStore store,
    IServiceProvider services,
    ILogger<GenreWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;

            try
            {
                delay = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Suggesting genres failed; trying again later");

                delay = AfterFailure;
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        await tracks.EnsureFreshAsync(IdleDelay, stoppingToken);

        SettleFromArtists(tracks.Current?.Rows ?? []);

        var asked = store.Read();

        var unasked = (tracks.Current?.Rows ?? [])
            .Where(row => asked.GetValueOrDefault(row.Song) is not { LookedUpAt: not null })
            .Select(row => (row.Song, Name: row.ArtistAndTitle))
            .DistinctBy(song => song.Song, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Resolved per tick: a typed HttpClient held for the life of the app outlives its handler.
        var lookup = services.GetRequiredService<GenreLookup>();
        var suggested = 0;

        foreach (var (song, name) in unasked)
        {
            if (name is not ({ } artist, { } title))
                continue;

            if (await lookup.HearAsync(artist, title, stoppingToken) is not { } heard)
                return AfterFailure;

            store.Suggest(song, heard, DateTimeOffset.UtcNow);
            suggested++;
        }

        if (suggested > 0)
            logger.LogInformation("Suggested genres for {Count} songs", suggested);

        return IdleDelay;
    }

    /// <summary>
    /// Settles every unanswered song whose artist the user's answers have settled, and takes back
    /// any such genre the answers no longer support.
    /// </summary>
    private void SettleFromArtists(IReadOnlyList<TrackRow> rows)
    {
        var records = store.Read();
        var songs = rows.Where(row => row.ArtistAndTitle is not null)
            .DistinctBy(row => row.Song, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
                (
                    row.Song,
                    Artist: ArtistCredit.Primary(row.ArtistAndTitle!.Value.Artist),
                    Record: records.GetValueOrDefault(row.Song)
                )
            )
            .ToArray();

        var learned = GenreByArtist.Learn(
            songs
                .Where(song => song.Record is { IsTheUsersAnswer: true })
                .Select(song => (song.Artist, song.Record!.Genre))
        );

        var changed = 0;

        foreach (var (song, artist, record) in songs)
        {
            // Never over the song's own sources: an artist's outliers name another genre (ADR 0033).
            var fits = learned.GetValueOrDefault(artist);
            var genre =
                fits is not null && (record?.Suggested is null || record.Suggested == fits)
                    ? fits
                    : null;

            if (
                record is { IsTheUsersAnswer: true }
                || record?.Genre == genre && record?.LearnedFrom is not null
            )
                continue;

            if (genre is not null)
                store.SettleFromArtist(song, genre, artist);
            else if (record?.LearnedFrom is not null)
                store.Unlearn(song);
            else
                continue;

            changed++;
        }

        if (changed > 0)
            logger.LogInformation(
                "Settled {Count} genres from the user's answers by artist",
                changed
            );
    }
}
