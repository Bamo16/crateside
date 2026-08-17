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
/// Pause carries no stated reason on purpose. Whether to leave the GPU alone right now is an
/// occasional judgement that belongs to the user, and every attempt to write down <em>why</em>
/// they might make it has so far been a guess rather than an observation.
/// </para>
/// <para>
/// <paramref name="startPaused"/> exists so that launching the app is never itself an act that
/// consumes the GPU — useful for a debugging session, a fresh checkout, or any period where
/// draining would conflict with work happening elsewhere. It changes where the toggle starts,
/// not what the default behaviour is.
/// </para>
/// </remarks>
public class DrainControl(bool startPaused = true)
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Released by <see cref="Resume"/> so the worker's idle wait ends immediately.
    /// </summary>
    /// <remarks>
    /// Without this, resuming only took effect on the loop's next tick — up to two minutes of
    /// "nothing to separate right now" while a track sat waiting, which reads as a broken button
    /// rather than a slow one. Bounded at one permit because the signal is level, not edge: two
    /// resumes in a row still mean "go", and a second permit would only make the following wait
    /// return instantly for no reason.
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

        Changed?.Invoke();
    }
}
