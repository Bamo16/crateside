using Crateside.Core.Genres;
using Crateside.Core.Tracks;

namespace Crateside;

/// <summary>
/// Asks the free sources for the labels of every song not asked yet, one at a time, and learns
/// genres from the person's answers. Never paused: it contends for nothing, and it writes no tag
/// (ADR 0088).
/// </summary>
public sealed class GenreWorker(
    TracksService tracks,
    GenreStore store,
    GenreLookup lookup,
    TimeProvider time,
    ILogger<GenreWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(30);

    private readonly NewWork _newWork = new(
        tracks,
        rows =>
            rows.Where(row =>
                    row is { Genre: not { LookedUpAt: not null }, ArtistAndTitle: not null }
                )
                .Select(row => row.Song)
    );

    public override void Dispose()
    {
        _newWork.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Looking up genres failed; trying again later");

                delay = AfterFailure;
            }

            await _newWork.WaitAsync(delay, stoppingToken);
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

        var suggested = 0;

        foreach (var (song, name) in unasked)
        {
            if (name is not ({ } artist, { } title))
                continue;

            if (await lookup.HearAsync(artist, title, stoppingToken) is not { } hearing)
            {
                logger.LogInformation(
                    "A genre source could not be reached for {Song}; trying again later",
                    song
                );

                return AfterFailure;
            }

            store.Hear(song, hearing, time.GetUtcNow());
            suggested++;
        }

        if (suggested > 0)
        {
            logger.LogInformation("Looked up genres for {Count} songs", suggested);
            await tracks.RefreshAsync(stoppingToken);
        }

        return IdleDelay;
    }

    /// <summary>
    /// Gives every unanswered song whose sources map onto nothing the genre the person's answers
    /// have settled for its artist, and takes back any such genre the answers no longer support.
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
                .SelectMany(song =>
                    song.Record!.Mine.Where(GenreVocabulary.IsGenre)
                        .Select(genre => (song.Artist, (string?)genre))
                )
        );

        var changed = 0;

        foreach (var (song, artist, record) in songs)
        {
            if (record is not { LookedUpAt: not null, IsTheUsersAnswer: false })
                continue;

            // Only where the song's own sources name nothing (ADR 0088).
            var genre = record.FromSources.Count is 0 ? learned.GetValueOrDefault(artist) : null;

            if (genre == record.Learned)
                continue;

            if (genre is not null)
                store.Learn(song, genre, artist);
            else
                store.Unlearn(song);

            changed++;
        }

        if (changed > 0)
            logger.LogInformation(
                "Settled {Count} genres from the user's answers by artist",
                changed
            );
    }
}
