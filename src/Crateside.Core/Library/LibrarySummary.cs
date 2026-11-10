using Crateside.Core.Watching;

namespace Crateside.Core.Library;

/// <summary>
/// Holds the last distribution read so the page does not re-scan the library to render, and reads
/// it again in the background once the library changes, at most every <see cref="LeastBetweenReads"/> (#155).
/// </summary>
public sealed class LibrarySummary(
    LibraryKeyReader reader,
    TimeProvider time,
    FileChanges? changes = null
)
{
    public static readonly TimeSpan LeastBetweenReads = TimeSpan.FromSeconds(15);

    private readonly Lock _gate = new();

    private KeyDistribution? _current;
    private long _readAtVersion;

    /// <summary>When the distribution held was read.</summary>
    public DateTimeOffset ReadAt { get; private set; }

    /// <summary>The read in the background, while there is one.</summary>
    internal Task? Refreshing { get; private set; }

    /// <summary>The distribution held, which a library change makes stale until its background read lands.</summary>
    public KeyDistribution Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is null)
                {
                    var version = changes?.LibraryVersion ?? 0;

                    _current = reader.Read();
                    _readAtVersion = version;
                    ReadAt = time.GetUtcNow();
                }
                else if (Refreshing is null && IsStale)
                    Refreshing = Task.Run(RefreshBehindTheScenes);

                return _current;
            }
        }
    }

    public KeyDistribution Refresh()
    {
        var version = changes?.LibraryVersion ?? 0;
        var read = reader.Read();

        lock (_gate)
        {
            ReadAt = time.GetUtcNow();
            _readAtVersion = version;

            return _current = read;
        }
    }

    private bool IsStale =>
        changes is not null
        && changes.LibraryVersion != _readAtVersion
        && time.GetUtcNow() - ReadAt >= LeastBetweenReads;

    private void RefreshBehindTheScenes()
    {
        try
        {
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally
        {
            lock (_gate)
                Refreshing = null;
        }
    }
}
