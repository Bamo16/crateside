namespace Crateside.Core.Orchestration;

/// <summary>
/// Whether the workers are draining, and what they are doing right now. Shared between the
/// background loop and the UI.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Draining is on by default.</strong> What limits separation is that two at once are
/// ruinous, not that one costs the user their machine — a single run is unnoticeable while they
/// work — so there is no moment to pick and nothing to start.
/// </para>
/// <para>
/// Pause exists for the reason that is actually true rather than the one originally assumed:
/// the GPU is audible, and a live microphone is a real reason to stop for an hour.
/// </para>
/// </remarks>
public sealed class DrainControl
{
    private readonly Lock _gate = new();

    private bool _paused;
    private string? _activity;

    /// <summary>Raised whenever the state changes, so a rendered page can catch up.</summary>
    public event Action? Changed;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
                return _paused;
        }
    }

    /// <summary>What is happening right now, or null when nothing is.</summary>
    public string? Activity
    {
        get
        {
            lock (_gate)
                return _activity;
        }
    }

    /// <summary>
    /// Stops new work being picked up. Does not interrupt a separation already running —
    /// that is what cancelling the loop does, and it is a heavier thing to ask for.
    /// </summary>
    public void Pause() => Set(paused: true);

    public void Resume() => Set(paused: false);

    /// <summary>Records what the worker is on, for the UI. Pass null when it goes idle.</summary>
    public void Report(string? activity)
    {
        lock (_gate)
        {
            if (_activity == activity)
                return;

            _activity = activity;
        }

        Changed?.Invoke();
    }

    private void Set(bool paused)
    {
        lock (_gate)
        {
            if (_paused == paused)
                return;

            _paused = paused;
        }

        Changed?.Invoke();
    }
}
