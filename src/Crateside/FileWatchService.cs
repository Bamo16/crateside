using Crateside.Core;
using Crateside.Core.Watching;

namespace Crateside;

/// <summary>Holds the folder watchers for as long as crateside runs.</summary>
public sealed class FileWatchService(CratesidePaths paths, FileChanges changes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var watchers = new FileWatchers(paths, changes);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }
}
