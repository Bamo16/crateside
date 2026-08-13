using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Keeps separation single-flight, across restarts as well as within one.
/// </summary>
/// <remarks>
/// <para>
/// Separation is ~6–7 minutes of GPU per track and must never run twice at once — concurrent
/// separations thrash the GPU and make the whole machine unusable. Within one process that is
/// easy. The hard case is a <em>restart</em>: on Windows a child process is not killed when its
/// parent exits, so a <c>stemforge-cli</c> crateside launched keeps running after crateside
/// goes away. A crateside that came back blind would see an unprocessed track and launch a
/// second one onto the same GPU.
/// </para>
/// <para>
/// So an orphan is <strong>adopted, not killed</strong>. Killing it would keep the bookkeeping
/// tidy at the cost of discarding up to two hours of GPU work on every hot reload, which is the
/// worse trade by a wide margin.
/// </para>
/// </remarks>
public sealed class SeparationInterlock(JobStore jobs, IProcessProbe probe)
{
    /// <summary>
    /// Squares the recorded jobs against what is actually running. Call this on startup, before
    /// anything is allowed to launch.
    /// </summary>
    public SeparationReconciliation Reconcile()
    {
        List<TrackJob> adopted = [];
        List<TrackJob> abandoned = [];

        foreach (var job in jobs.Open(JobStage.Separation))
        {
            if (IsStillRunning(job))
            {
                adopted.Add(job);
                continue;
            }

            // The process is gone and nothing recorded a result, so it did not finish: the
            // machine rebooted, or it was killed. That is a real failure and earns its strike.
            abandoned.Add(
                jobs.Record(
                    job.VideoId,
                    JobStage.Separation,
                    JobStatus.Failed,
                    error: job.ProcessId is { } pid
                        ? $"separation process {pid} is gone without recording a result"
                        : "separation was interrupted before it started"
                )
            );
        }

        return new SeparationReconciliation { Adopted = adopted, Abandoned = abandoned };
    }

    /// <summary>
    /// The separation happening right now, if there is one. This is the gate every launch must
    /// pass through.
    /// </summary>
    public bool TryGetLive([NotNullWhen(true)] out TrackJob? job)
    {
        job = jobs.Open(JobStage.Separation).FirstOrDefault(IsStillRunning);

        return job is not null;
    }

    /// <summary>Records that a separation process has been launched and now owns the GPU.</summary>
    public TrackJob Claim(string videoId, int processId, DateTimeOffset processStartedAt) =>
        jobs.Record(videoId, JobStage.Separation, JobStatus.Running, processId, processStartedAt);

    /// <summary>Records how a separation ended, freeing the GPU for the next one.</summary>
    public TrackJob Release(string videoId, string? error = null) =>
        jobs.Record(
            videoId,
            JobStage.Separation,
            error is null ? JobStatus.Succeeded : JobStatus.Failed,
            error: error
        );

    private bool IsStillRunning(TrackJob job)
    {
        // Queued but never launched: no process was ever started, so nothing is running.
        if (job.ProcessId is not { } processId)
            return false;

        if (!probe.TryGetStartTime(processId, out var startedAt))
            return false;

        // A live PID with no recorded start time cannot be verified. Treat it as ours, because
        // the two mistakes are not symmetrical: a wrong adopt costs a delay, while a wrong
        // launch puts two jobs on the GPU — the exact thing this class exists to prevent.
        if (job.ProcessStartedAt is not { } recorded)
            return true;

        // Whole seconds, because the value has been through JSON since it was measured.
        return Math.Abs((startedAt - recorded).TotalSeconds) < 1;
    }
}

/// <summary>What startup found: which separations were still going, and which had died.</summary>
public sealed record SeparationReconciliation
{
    /// <summary>Still running, and now being watched again by this crateside.</summary>
    public required IReadOnlyList<TrackJob> Adopted { get; init; }

    /// <summary>Recorded as running but gone; marked failed and eligible to be retried.</summary>
    public required IReadOnlyList<TrackJob> Abandoned { get; init; }

    public bool FoundAnything => Adopted.Count > 0 || Abandoned.Count > 0;
}
