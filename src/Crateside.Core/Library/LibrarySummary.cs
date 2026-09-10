namespace Crateside.Core.Library;

/// <summary>
/// Holds the last distribution read, so the page does not re-scan the library to render.
/// </summary>
/// <remarks>
/// Deliberately has no expiry. The library changes when the user finishes a track, which is
/// minutes of work apart, so a stale count is corrected by asking rather than by a timer.
/// </remarks>
public sealed class LibrarySummary(LibraryKeyReader reader)
{
    private readonly Lock _gate = new();

    private KeyDistribution? _current;

    public KeyDistribution Current
    {
        get
        {
            lock (_gate)
                return _current ??= reader.Read();
        }
    }

    public KeyDistribution Refresh()
    {
        var read = reader.Read();

        lock (_gate)
            return _current = read;
    }
}
