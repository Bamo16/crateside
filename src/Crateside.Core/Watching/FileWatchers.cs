using Crateside.Core.Samples;

namespace Crateside.Core.Watching;

/// <summary>
/// Watches Staging's top level and the whole library for audio, sidecars and folders appearing,
/// moving or going, and says so to <see cref="FileChanges"/>. A wake-up only: events are dropped
/// under load and while crateside is down, so every worker keeps its timer (#132).
/// </summary>
public sealed class FileWatchers : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];

    public FileWatchers(CratesidePaths paths, FileChanges changes)
    {
        Watch(paths.StagingFolder, includeSubdirectories: false, changes);
        Watch(paths.LibraryFolder, includeSubdirectories: true, changes);
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
    }

    private void Watch(string root, bool includeSubdirectories, FileChanges changes)
    {
        if (!Directory.Exists(root))
            return;

        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = includeSubdirectories,
                InternalBufferSize = 64 * 1024,
                NotifyFilter =
                    NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
            };

            void Changed(object? sender, FileSystemEventArgs e)
            {
                if (Matters(e.FullPath))
                    changes.Notify();
            }

            watcher.Created += Changed;
            watcher.Changed += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += (_, e) =>
            {
                if (Matters(e.FullPath) || Matters(e.OldFullPath))
                    changes.Notify();
            };
            // An overflow says only that events were lost; the scan covers what was missed.
            watcher.Error += (_, _) => changes.Notify();
            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
        }
        catch (Exception ex)
            when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            // The timer covers a root that cannot be watched.
        }
    }

    // A path with no extension is a folder, such as a track moved whole.
    private static bool Matters(string path) =>
        SampleFiles.IsAudio(path)
        || Path.GetExtension(path) is { Length: 0 }
        || Path.GetExtension(path).Equals(".asd", StringComparison.OrdinalIgnoreCase);
}
