using Crateside.Core.Orchestration;

namespace Crateside;

/// <summary>
/// Sweeps finished working sets, slowly. Import happens on human timescales — a track can sit in
/// staging for weeks — so there is nothing to be gained by looking often.
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
            catch (Exception e)
            {
                // Scratch that outlives its welcome costs disk and nothing else.
                logger.LogWarning(e, "Working-set sweep failed");
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
