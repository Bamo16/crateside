using Crateside.Core.Warps;
using Humanizer;

namespace Crateside;

/// <summary>
/// Gives the saved clips with Warp off that lack Live's marker pair that pair, once after start and
/// again for any an open set held back, then stops (ADR 0091).
/// </summary>
public sealed class WarpOffRepairWorker(WarpOffRepair repair, ILogger<WarpOffRepairWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstPass;

        while (!stoppingToken.IsCancellationRequested && !repair.IsDone)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                delay = Interval;

                var (repaired, deferred) = repair.Run();

                foreach (var path in repaired)
                    logger.LogInformation(
                        "Gave {Sample}'s clip with Warp off the marker pair Live needs",
                        path
                    );

                logger.LogInformation(
                    "Warp-off repair: {Repaired} repaired, {Deferred} left for later because an open set uses them",
                    repaired.Count,
                    deferred.Count
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Repairing clips with Warp off failed; trying again later");
            }
        }
    }
}
