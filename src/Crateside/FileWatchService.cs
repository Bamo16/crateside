using Crateside.Core;
using Crateside.Core.Watching;

namespace Crateside;

/// <summary>Holds the folder watchers for as long as crateside runs.</summary>
public sealed class FileWatchService(
    CratesidePaths paths,
    FileChanges changes,
    ILogger<FileWatchService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (paths is not { StagingFolder: { Length: > 0 }, LibraryFolder: { Length: > 0 } })
            logger.LogWarning(
                "The Staging or Library folder is not set; choose them in Settings and restart"
            );

        using var watchers = new FileWatchers(paths, changes);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }
}
