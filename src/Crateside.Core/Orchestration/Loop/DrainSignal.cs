namespace Crateside.Core.Orchestration.Loop;

/// <summary>Ends a stage's idle wait early, so a change does not wait out the interval.</summary>
public class DrainSignal
{
    // One permit at most: a second would end the following wait for no reason.
    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>
    /// Ends the worker's idle wait without changing whether it is paused, which
    /// <see cref="DrainControl.Resume"/> cannot do.
    /// </summary>
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
