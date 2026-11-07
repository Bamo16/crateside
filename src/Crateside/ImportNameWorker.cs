using Crateside.Core.Imports;

namespace Crateside;

/// <summary>
/// Names the staged files of imported tracks after their tags (ADR 0079). A rename an open set
/// refuses is tried again later.
/// </summary>
public sealed class ImportNameWorker(ImportNameRepair repair, ILogger<ImportNameWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstPass;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                delay = Interval;

                var (renamed, refused) = repair.Run();

                foreach (var trackKey in renamed)
                    logger.LogInformation(
                        "Named {TrackKey}'s staged files after its tags",
                        trackKey
                    );

                foreach (var trackKey in refused)
                    logger.LogInformation(
                        "Could not name {TrackKey}'s staged files after its tags yet",
                        trackKey
                    );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Naming imported tracks' files failed; trying again later");
            }
        }
    }
}
