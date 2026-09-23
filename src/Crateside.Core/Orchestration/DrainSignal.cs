namespace Crateside.Core.Orchestration;

/// <summary>Ends a stage's idle wait early, so a change does not wait out the interval.</summary>
public class DrainSignal
{
    /// <remarks>
    /// Bounded at one permit: the signal is level, not edge, so a second permit would only make
    /// the following wait return instantly for no reason.
    /// </remarks>
    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>
    /// Ends the worker's idle wait without changing whether it is paused.
    /// </summary>
    /// <remarks>
    /// <see cref="DrainControl.Resume"/> cannot serve here: it is level-triggered, so calling it
    /// when nothing is paused releases nothing and the worker sleeps out its full delay.
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

    /// <summary>Waits out the worker's idle delay, returning early when woken.</summary>
    /// <returns>True when woken, false when the delay simply elapsed.</returns>
    public Task<bool> WaitForWorkAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default
    ) => _wake.WaitAsync(delay, cancellationToken);
}
