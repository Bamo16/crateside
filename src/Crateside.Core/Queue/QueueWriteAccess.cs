namespace Crateside.Core.Queue;

/// <summary>
/// Whether crateside can still write to the playlist, and what happened when it last tried.
/// </summary>
/// <remarks>
/// The write token lapses roughly weekly while the OAuth client is unapproved, and nothing
/// automatic can renew it — re-consent is interactive. So a lapse is reported until the user
/// acts on it, rather than once.
/// </remarks>
public sealed class QueueWriteAccess
{
    private readonly Lock _gate = new();

    private string? _reason;
    private DateTimeOffset? _lapsedAt;
    private DateTimeOffset? _toldAt;
    private IReadOnlyList<RemovalResult> _failures = [];

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

    /// <summary>Entries the last consume tried to remove and could not, with the reason each gave.</summary>
    /// <remarks>
    /// <see cref="IntakeStore.MarkRemovalAttempted"/> runs before the delete and is never cleared,
    /// so nothing will try these again — this list is the only record that they were left behind.
    /// </remarks>
    public IReadOnlyList<RemovalResult> Failures
    {
        get
        {
            lock (_gate)
                return _failures;
        }
    }

    /// <summary>Records that consent no longer authorizes a write.</summary>
    public void Lapsed(string reason)
    {
        lock (_gate)
        {
            // Kept from the first report: how long it has been lapsed is the useful figure, and
            // every later attempt fails the same way.
            _lapsedAt ??= DateTimeOffset.UtcNow;
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

    /// <summary>Keeps what a consume could not remove, replacing the previous run's.</summary>
    public void Record(IReadOnlyList<RemovalResult> results)
    {
        var failures = results.Where(r => !r.Removed).ToArray();

        lock (_gate)
            _failures = failures;

        if (failures.Length > 0)
            Changed?.Invoke();
    }

    /// <summary>
    /// Whether the user should be told now, true at most once per <paramref name="every"/> while
    /// the lapse stands.
    /// </summary>
    /// <remarks>Asking marks it told, so two callers do not each get a turn.</remarks>
    public bool ShouldTell(TimeSpan every)
    {
        lock (_gate)
        {
            if (_lapsedAt is null)
                return false;

            var now = DateTimeOffset.UtcNow;

            if (_toldAt is { } told && now - told < every)
                return false;

            _toldAt = now;

            return true;
        }
    }
}
