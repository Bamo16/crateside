using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Whether the verdict stage is running, and what it is doing right now.</summary>
public sealed class VerdictControl : DrainControl
{
    private int _formRunsWaiting;

    /// <summary>Runs started from a track's page that are waiting for the judgment in progress (#120).</summary>
    public int FormRunsWaiting => Volatile.Read(ref _formRunsWaiting);

    /// <summary>Paused or not as the user last left judging.</summary>
    public VerdictControl(PauseStore remembered)
        : base(remembered.Read().Judging) { }

    internal VerdictControl(bool startPaused = true)
        : base(startPaused) { }

    /// <summary>Counts a form run as waiting until the returned scope is disposed.</summary>
    public IDisposable WaitingFormRun()
    {
        Interlocked.Increment(ref _formRunsWaiting);
        RaiseChanged();

        return new Waiting(this);
    }

    private sealed class Waiting(VerdictControl control) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
                return;

            Interlocked.Decrement(ref control._formRunsWaiting);
            control.RaiseChanged();
        }
    }
}
