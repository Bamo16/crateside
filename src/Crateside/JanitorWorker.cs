using Crateside.Core.Orchestration;

namespace Crateside;

/// <summary>
/// Sweeps finished working sets on a slow interval — a track can sit in staging for weeks
/// before import.
/// </summary>
public sealed class JanitorWorker(WorkingSetJanitor janitor, ILogger<JanitorWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (janitor.Sweep() is > 0 and var discarded)
                    logger.LogInformation(
                        "Discarded {Count} working sets for imported tracks",
                        discarded
                    );
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Working-set sweep failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
