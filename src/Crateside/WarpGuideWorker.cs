using Crateside.Core.Staging;

namespace Crateside;

/// <summary>
/// Keeps each filed track's warp guide outside Ableton once a minute. Moving a file contends for
/// nothing, so this runs unattended like detection (ADR 0046).
/// </summary>
public sealed class WarpGuideWorker(WarpGuideKeeper keeper, ILogger<WarpGuideWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var keeping in keeper.Keep())
                    Log(keeping);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Keeping warp guides failed");
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

    private void Log(GuideKeeping keeping)
    {
        switch (keeping)
        {
            case GuideKeeping.Kept(var from, var to):
                logger.LogInformation(
                    "Kept the warp guide {Guide} in {Folder}",
                    Path.GetFileName(from),
                    to
                );

                break;

            case GuideKeeping.Failed(var guide, var error):
                logger.LogWarning(
                    "Could not keep the warp guide {Guide}, trying again in a minute: {Error}",
                    Path.GetFileName(guide),
                    error
                );

                break;
        }
    }
}
