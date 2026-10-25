namespace Crateside.Core.Orchestration.Loop;

/// <summary>
/// Whether the workers are draining, and what they are doing right now. Shared between the
/// background loop and the UI — for the stages that spend the GPU or quota (ADR 0024).
/// </summary>
public class DrainControl : DrainSignal
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
    /// that is what canceling the loop does, and it is a heavier thing to ask for.
    /// </summary>
    /// <summary>Separation's control, paused or not as the user last left it.</summary>
    public DrainControl(PauseStore remembered)
        : this(remembered.Read().Separation) { }

    internal DrainControl(bool startPaused = true) => _paused = startPaused;

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

    protected void RaiseChanged() => Changed?.Invoke();

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
