namespace Crateside.Core.Orchestration;

/// <summary>
/// Whether the workers are draining, and what they are doing right now. Shared between the
/// background loop and the UI.
/// </summary>
/// <remarks>
/// Only a stage that contends for something scarce needs one. Every stage is wakeable; this adds
/// pausing, which is the GPU's and the quota's concern alone (ADR 0024).
/// </remarks>
public class DrainControl(bool startPaused = true) : DrainSignal
{
    private readonly Lock _gate = new();

    private bool _paused = startPaused;
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

        if (!paused)
            Wake();

        Changed?.Invoke();
    }
}
