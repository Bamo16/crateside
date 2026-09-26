namespace Crateside.Core.Quota;

/// <summary>
/// Whether an idle five-hour window should judge one library backfill track (ADR 0029, ADR 0035).
/// A window that cannot be read is never idle.
/// </summary>
public sealed class WindowRoll(bool enabled = false, DateTimeOffset? rolledAt = null)
{
    /// <summary>How long a window must read 0% before it counts as idle rather than just turned over.</summary>
    public static readonly TimeSpan IdleFor = TimeSpan.FromMinutes(15);

    /// <summary>At most one roll per window, whatever the readings say in between.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(5);

    private readonly Lock _gate = new();

    private bool _enabled = enabled;
    private DateTimeOffset? _idleSince;
    private DateTimeOffset? _rolledAt = rolledAt;

    /// <summary>Raised whenever the state changes, so a rendered page can catch up.</summary>
    public event Action? Changed;

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _enabled;
        }
    }

    /// <summary>When a verdict of ours last opened a window, or null when none has this run.</summary>
    public DateTimeOffset? RolledAt
    {
        get
        {
            lock (_gate)
                return _rolledAt;
        }
    }

    public void Enable() => Set(enabled: true);

    public void Disable() => Set(enabled: false);

    /// <summary>
    /// Whether this reading, taken at <paramref name="now"/>, is a window nobody has touched for
    /// <see cref="IdleFor"/> and that no roll of ours has already opened.
    /// </summary>
    public bool ShouldRoll(QuotaSnapshot quota, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_enabled || quota is not { Utilization: <= 0 })
            {
                _idleSince = null;

                return false;
            }

            _idleSince ??= now;

            return now - _idleSince >= IdleFor
                && !(_rolledAt is { } rolled && now - rolled < Window);
        }
    }

    /// <summary>Records that a verdict was run to open the window.</summary>
    public void MarkRolled(DateTimeOffset now)
    {
        lock (_gate)
        {
            _rolledAt = now;
            _idleSince = null;
        }

        Changed?.Invoke();
    }

    private void Set(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled)
                return;

            _enabled = enabled;
            _idleSince = null;
        }

        Changed?.Invoke();
    }
}
