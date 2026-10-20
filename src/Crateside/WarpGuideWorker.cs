using Crateside.Core.Staging;
using Crateside.Core.Watching;

namespace Crateside;

/// <summary>
/// Keeps each filed track's warp guide outside Ableton, when Staging or the library changes and
/// every ten minutes besides. Moving a file contends for
/// nothing, so this runs unattended like detection (ADR 0046).
/// </summary>
public sealed class WarpGuideWorker(
    WarpGuideKeeper keeper,
    FileChanges changes,
    ILogger<WarpGuideWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly FileChanges.Listener _wake = changes.Listen();

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
                await _wake.WaitAsync(Interval, stoppingToken);
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
                    "Could not keep the warp guide {Guide}, trying again at the next change: {Error}",
                    Path.GetFileName(guide),
                    error
                );

                break;
        }
    }
}
