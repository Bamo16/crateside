namespace Crateside.Core.Playlist;

/// <summary>
/// Whether crateside can still write to the playlist, and what happened when it last tried. Only
/// the user can renew a lapse, so it is reported until they do.
/// </summary>
public sealed class PlaylistWriteAccess(TimeProvider time)
{
    private readonly Lock _gate = new();

    private string? _reason;
    private DateTimeOffset? _lapsedAt;
    private DateTimeOffset? _toldAt;

    /// <summary>Raised when the state changes, so a rendered page can catch up.</summary>
    public event Action? Changed;

    /// <summary>Whether the stored consent can no longer authorize a write.</summary>
    public bool HasLapsed
    {
        get
        {
            lock (_gate)
                return _lapsedAt is not null;
        }
    }

    /// <summary>Why writing is refused, in the API's own words where it gave any.</summary>
    public string? Reason
    {
        get
        {
            lock (_gate)
                return _reason;
        }
    }

    public DateTimeOffset? LapsedAt
    {
        get
        {
            lock (_gate)
                return _lapsedAt;
        }
    }

    /// <summary>Records that consent no longer authorizes a write.</summary>
    public void Lapsed(string reason)
    {
        lock (_gate)
        {
            // Kept from the first report: how long it has been lapsed is the useful figure, and
            // every later attempt fails the same way.
            _lapsedAt ??= time.GetUtcNow();
            _reason = reason;
        }

        Changed?.Invoke();
    }

    /// <summary>Records that a write was authorized, clearing any lapse.</summary>
    public void Works()
    {
        lock (_gate)
        {
            if (_lapsedAt is null)
                return;

            _lapsedAt = null;
            _reason = null;
            _toldAt = null;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Whether the user should be told now, true at most once per <paramref name="every"/> while
    /// the lapse stands. Asking marks it told.
    /// </summary>
    public bool ShouldTell(TimeSpan every)
    {
        lock (_gate)
        {
            if (_lapsedAt is null)
                return false;

            var now = time.GetUtcNow();

            if (_toldAt is { } told && now - told < every)
                return false;

            _toldAt = now;

            return true;
        }
    }
}
