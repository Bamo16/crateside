using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;
using Humanizer;

namespace Crateside.Core.Orchestration.Separation;

/// <summary>
/// Keeps separation single-flight across restarts, adopting a <c>stemforge-cli</c> that outlived
/// one and reading its result off disk once it ends (ADR 0042).
/// </summary>
public sealed class SeparationInterlock(
    JobStore jobs,
    IProcessProbe probe,
    CratesidePaths? paths = null
)
{
    /// <summary>
    /// Squares the recorded jobs against what is actually running. Call this on startup, before
    /// anything is allowed to launch.
    /// </summary>
    public SeparationReconciliation Reconcile()
    {
        List<TrackJob> adopted = [];
        List<TrackJob> unlaunched = [];

        foreach (var job in jobs.Open(JobStage.Separation).Where(job => job.ProcessId is null))
            unlaunched.Add(
                jobs.Record(
                    job.TrackKey,
                    JobStage.Separation,
                    JobStatus.Failed,
                    error: "separation was interrupted before it started"
                )
            );

        foreach (var job in jobs.Open(JobStage.Separation).Where(IsStillRunning))
            adopted.Add(
                jobs.Record(
                    job.TrackKey,
                    JobStage.Separation,
                    JobStatus.Running,
                    job.ProcessId,
                    job.ProcessStartedAt,
                    adopted: true
                )
            );

        var settled = SettleEnded();

        return settled with
        {
            Adopted = adopted,
            Abandoned = [.. unlaunched, .. settled.Abandoned],
        };
    }

    /// <summary>
    /// Records how every launched separation whose process has ended turned out, reading what it
    /// wrote off disk, since nobody was left to read its output.
    /// </summary>
    public SeparationReconciliation SettleEnded()
    {
        List<SettledSeparation> finished = [];
        List<TrackJob> abandoned = [];

        foreach (
            var job in jobs.Open(JobStage.Separation)
                .Where(job => job.ProcessId is not null && !IsStillRunning(job))
        )
        {
            if (Settle(job) is { } settled)
                finished.Add(settled);
            else if (jobs.TryGet(job.TrackKey, JobStage.Separation, out var failed))
                abandoned.Add(failed);
        }

        return new SeparationReconciliation
        {
            Adopted = [],
            Finished = finished,
            Abandoned = abandoned,
        };
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
    public bool IsSeparating(string trackKey) =>
        jobs.TryGet(trackKey, JobStage.Separation, out var job)
        && job.IsOpen
        && IsStillRunning(job);

    /// <summary>Records that a separation process has been launched and now owns the GPU.</summary>
    public TrackJob Claim(
        string trackKey,
        int processId,
        DateTimeOffset processStartedAt,
        SeparationPromise? promise = null
    ) =>
        jobs.Record(
            trackKey,
            JobStage.Separation,
            JobStatus.Running,
            processId,
            processStartedAt,
            promise: promise
        );

    /// <summary>Records how a separation ended, freeing the GPU for the next one.</summary>
    public TrackJob Release(string trackKey, string? error = null, string? summary = null) =>
        jobs.Record(
            trackKey,
            JobStage.Separation,
            error is null ? JobStatus.Succeeded : JobStatus.Failed,
            error: error,
            summary: error is null ? summary : null
        );

    /// <summary>The separation, recorded as succeeded, or null once it is recorded as failed.</summary>
    private SettledSeparation? Settle(TrackJob job)
    {
        if (job is not { ProcessId: { } processId, Promise: { } promise } || paths is null)
        {
            // Launched before crateside recorded what a run promised.
            jobs.Record(
                job.TrackKey,
                JobStage.Separation,
                JobStatus.Failed,
                error: $"separation process {job.ProcessId} is gone without recording a result"
            );

            return null;
        }

        var outputs = WrittenSince(job, paths.DestinationFor(promise.Destination));

        if (outputs.Count < promise.ExpectedOutputs)
        {
            jobs.Record(
                job.TrackKey,
                JobStage.Separation,
                JobStatus.Failed,
                error: $"separation process {processId} ended having written "
                    + $"{"file".ToQuantity(outputs.Count)} of the {promise.ExpectedOutputs} it was launched for"
            );

            return null;
        }

        return new SettledSeparation(
            Release(
                job.TrackKey,
                summary: $"{"file".ToQuantity(outputs.Count)} written while crateside was not watching"
            ),
            promise,
            outputs
        );
    }

    private static IReadOnlyList<string> WrittenSince(TrackJob job, string directory) =>
        [
            .. StemTagReader
                .FilesCutFrom(directory, job.TrackKey)
                // A stem from an earlier run of the same track is not this run's output.
                .Where(file =>
                    job.ProcessStartedAt is not { } started
                    || File.GetLastWriteTimeUtc(file) >= started.UtcDateTime.AddSeconds(-1)
                ),
        ];

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

/// <summary>What a reconciliation found: which separations were still going, and how the rest ended.</summary>
public sealed record SeparationReconciliation
{
    /// <summary>Still running, and now being watched again by this crateside.</summary>
    public required IReadOnlyList<TrackJob> Adopted { get; init; }

    /// <summary>Ended with everything it was launched for on disk; recorded as succeeded.</summary>
    public required IReadOnlyList<SettledSeparation> Finished { get; init; }

    /// <summary>Ended short, or with no record of what it was for; marked failed and eligible to be retried.</summary>
    public required IReadOnlyList<TrackJob> Abandoned { get; init; }

    public bool FoundAnything => Adopted.Count > 0 || Finished.Count > 0 || Abandoned.Count > 0;
}

/// <summary>A separation that ended while nobody was reading its output, and the stems it wrote.</summary>
public sealed record SettledSeparation(
    TrackJob Job,
    SeparationPromise Promise,
    IReadOnlyList<string> OutputFiles
);
