namespace Crateside.Core.Orchestration;

/// <summary>
/// Whether the workers are draining, and what they are doing right now. Shared between the
/// background loop and the UI.
/// </summary>
public class DrainControl(bool startPaused = true)
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Released by <see cref="Resume"/> so the worker's idle wait ends immediately.
    /// </summary>
    /// <remarks>
    /// Bounded at one permit: the signal is level, not edge, so a second permit would only make
    /// the following wait return instantly for no reason.
    /// </remarks>
    private readonly SemaphoreSlim _wake = new(0, 1);

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

    /// <summary>
    /// Ends the worker's idle wait without changing whether it is paused.
    /// </summary>
    /// <remarks>
    /// <see cref="Resume"/> cannot serve here: it is level-triggered, so calling it when nothing
    /// is paused releases nothing and the worker sleeps out its full delay.
    /// </remarks>
    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled and not yet consumed — the worker is about to run anyway.
        }
    }

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

    /// <summary>
    /// Waits out the worker's idle delay, returning early when <see cref="Resume"/> is called.
    /// </summary>
    /// <returns>True when woken by a resume, false when the delay simply elapsed.</returns>
    public Task<bool> WaitForWorkAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default
    ) => _wake.WaitAsync(delay, cancellationToken);

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
