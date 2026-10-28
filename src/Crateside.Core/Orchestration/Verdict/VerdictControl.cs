using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Whether the verdict stage is running, and what it is doing right now.</summary>
public sealed class VerdictControl : DrainControl
{
    private readonly Lock _holdGate = new();
    private int _formRunsWaiting;
    private JudgeHold? _hold;

    /// <summary>Why the verdict stage is holding off by itself, or null when it is not.</summary>
    public JudgeHold? Hold
    {
        get
        {
            lock (_holdGate)
                return _hold;
        }
    }

    /// <summary>Runs started from a track's page that are waiting for the judgment in progress (#120).</summary>
    public int FormRunsWaiting => Volatile.Read(ref _formRunsWaiting);

    /// <summary>Paused or not as the user last left judging.</summary>
    public VerdictControl(PauseStore remembered)
        : base(remembered.Read().Judging) { }

    internal VerdictControl(bool startPaused = true)
        : base(startPaused) { }

    /// <summary>Records that the stage is holding off, or clears it with null.</summary>
    public void SetHold(JudgeHold? hold)
    {
        lock (_holdGate)
        {
            if (_hold == hold)
                return;

            _hold = hold;
        }

        RaiseChanged();
    }

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

/// <summary>Why the verdict stage waits, and when it tries again where the endpoint said.</summary>
public sealed record JudgeHold(string Reason, DateTimeOffset? RetryAt);
