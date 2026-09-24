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

        var asked = store.Read();

        var unasked = (tracks.Current?.Rows ?? [])
            .Where(row => asked.GetValueOrDefault(row.Song) is not { LookedUpAt: not null })
            .Select(row => (row.Song, Name: NameOf(row)))
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

    /// <summary>The ledger's artist and title, else the row's own name split at its dash.</summary>
    private static (string Artist, string Title)? NameOf(TrackRow row) =>
        row.Verdict is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
            ? (artist, title)
        : row.Title.Split(" - ", 2) is [{ Length: > 0 } a, { Length: > 0 } t] ? (a, t)
        : null;
}
