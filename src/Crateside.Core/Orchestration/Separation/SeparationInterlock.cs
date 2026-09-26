using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Orchestration.Separation;

/// <summary>
/// Keeps separation single-flight across restarts, adopting a <c>stemforge-cli</c> that outlived
/// one rather than killing it.
/// </summary>
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
                adopted.Add(
                    jobs.Record(
                        job.VideoId,
                        JobStage.Separation,
                        JobStatus.Running,
                        job.ProcessId,
                        job.ProcessStartedAt,
                        adopted: true
                    )
                );
                continue;
            }

            // Gone with no result recorded — the machine rebooted, or the process was killed.
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

    /// <summary>The separation happening right now, if there is one — the gate every launch must pass through.</summary>
    public bool TryGetLive([NotNullWhen(true)] out TrackJob? job)
    {
        job = jobs.Open(JobStage.Separation).FirstOrDefault(IsStillRunning);

        return job is not null;
    }

    /// <summary>
    /// Whether this track's separation process is alive right now, and so may still be writing
    /// files; a recorded <c>Running</c> whose process is gone says nothing.
    /// </summary>
    public bool IsSeparating(string videoId) =>
        jobs.TryGet(videoId, JobStage.Separation, out var job) && job.IsOpen && IsStillRunning(job);

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

        // Unverifiable PID is treated as running: a wrong adopt costs a delay, a wrong launch
        // double-books the GPU.
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
