using Crateside.Core.Tracks;

namespace Crateside;

/// <summary>Moves records kept under a video ID onto their song once the ledger has judged it, so no read has to save them (ADR 0071).</summary>
public sealed class SongKeyWorker(SongKeyMigration migration, ILogger<SongKeyWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);

                if (migration.Run() is > 0 and var moved)
                    logger.LogInformation(
                        "Moved {Count} genre, familiarity and mark records from video IDs to their songs",
                        moved
                    );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Moving records from video IDs to songs failed");
            }
        }
    }
}
