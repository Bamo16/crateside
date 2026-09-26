using Crateside.Core.Familiarity;
using Crateside.Core.Tracks;

namespace Crateside;

/// <summary>
/// Asks Deezer how far every song reaches that it has not been asked about, one at a time. Never
/// paused: it contends for nothing, and the backing pass is what writes the tag (#52).
/// </summary>
public sealed class FamiliarityWorker(
    TracksService tracks,
    FamiliarityStore store,
    IServiceProvider services,
    ILogger<FamiliarityWorker> logger
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Measuring familiarity failed; trying again later");

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
            .Where(row => asked.GetValueOrDefault(row.Song) is null or { IsOwedALookup: true })
            .Select(row => (row.Song, Name: row.ArtistAndTitle))
            .DistinctBy(song => song.Song, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Resolved per tick: a typed HttpClient held for the life of the app outlives its handler.
        var lookup = services.GetRequiredService<FamiliarityLookup>();
        var measured = 0;

        foreach (var (song, name) in unasked)
        {
            if (name is not ({ } artist, { } title))
                continue;

            if (await lookup.ReadAsync(artist, title, stoppingToken) is not { } reading)
                return AfterFailure;

            store.Measure(song, reading, DateTimeOffset.UtcNow, FamiliarityLookup.Version);
            measured++;
        }

        if (measured > 0)
            logger.LogInformation("Measured familiarity for {Count} songs", measured);

        return IdleDelay;
    }
}
