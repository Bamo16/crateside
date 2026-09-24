namespace Crateside.Core.Library;

/// <summary>
/// Holds the last distribution read, with no expiry, so the page does not re-scan the library to
/// render.
/// </summary>
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
