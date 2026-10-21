namespace Crateside.Core.Library;

/// <summary>
/// Holds the last distribution read, with no expiry, so the page does not re-scan the library to
/// render.
/// </summary>
public sealed class LibrarySummary(LibraryKeyReader reader, TimeProvider time)
{
    private readonly Lock _gate = new();

    private KeyDistribution? _current;

    /// <summary>When the distribution held was read.</summary>
    public DateTimeOffset ReadAt { get; private set; }

    public KeyDistribution Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is null)
                {
                    _current = reader.Read();
                    ReadAt = time.GetUtcNow();
                }

                return _current;
            }
        }
    }

    public KeyDistribution Refresh()
    {
        var read = reader.Read();

        lock (_gate)
        {
            ReadAt = time.GetUtcNow();

            return _current = read;
        }
    }
}
