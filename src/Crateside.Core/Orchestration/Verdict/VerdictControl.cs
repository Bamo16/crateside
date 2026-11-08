using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Whether the verdict stage is running, and what it is doing right now.</summary>
public sealed class VerdictControl : DrainControl
{
    private readonly Lock _holdGate = new();
    private int _formRunsWaiting;
    private JudgeHold? _hold;
    private string? _judgingTrackKey;

    /// <summary>The track a session is judging right now, or null between sessions.</summary>
    public string? JudgingTrackKey => Volatile.Read(ref _judgingTrackKey);

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

    /// <summary>How long judging holds after the judge's endpoint refused a session (ADR 0070).</summary>
    public JudgeBackoff Backoff { get; }

    /// <summary>Paused or not as the user last left judging.</summary>
    public VerdictControl(PauseStore remembered, TimeProvider time)
        : base(
            remembered.Read()
                is { Judging: true }
                    or { Backoff.Refusals: >= JudgeBackoff.PauseAfter },
            time
        ) => Backoff = new JudgeBackoff(time, remembered);

    internal VerdictControl(bool startPaused = true, TimeProvider? time = null)
        : base(startPaused, time) => Backoff = new JudgeBackoff(time ?? TimeProvider.System);

    /// <summary>Whether judging paused itself after a run of refusals, and so says why until the user resumes it (#181).</summary>
    public bool IsPausedByRefusals => IsPaused && Backoff.IsExhausted;

    /// <summary>The user's resume also clears the refusals and their hold, so the next pass tries at once (#181).</summary>
    public override void Resume()
    {
        Backoff.Clear();
        SetHold(null);
        base.Resume();
    }

    /// <summary>Records which track a session is judging, so the UI can show it before the Tracks rows catch up.</summary>
    public void ReportJudging(string trackKey, string activity)
    {
        Volatile.Write(ref _judgingTrackKey, trackKey);
        Report(activity);
    }

    /// <summary>Records that no session is judging.</summary>
    public void ReportIdle()
    {
        Volatile.Write(ref _judgingTrackKey, null);
        Report(null);
    }

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
