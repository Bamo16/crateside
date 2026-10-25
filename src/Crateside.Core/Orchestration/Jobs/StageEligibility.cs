using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// Whether a stage may attempt a track again, given what crateside has already recorded running
/// for it.
/// </summary>
public static class StageEligibility
{
    /// <summary>How many failures a stage takes before it leaves a track alone.</summary>
    public static int MaxStrikesFor(JobStage stage) =>
        stage switch
        {
            JobStage.Download => DownloadSelector.DefaultMaxStrikes,
            JobStage.Evidence => EvidenceSelector.DefaultMaxStrikes,
            JobStage.Verdict => VerdictSelector.DefaultMaxStrikes,
            JobStage.Separation => SeparationSelector.DefaultMaxStrikes,
            JobStage.Handoff => HandoffSelector.DefaultMaxStrikes,
            JobStage.Tagging => TaggingSelector.DefaultMaxStrikes,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };

    /// <summary>
    /// Whether this job is work still to be done: open, or failed with strikes left, but never
    /// given up on (ADR 0025, #32).
    /// </summary>
    public static bool IsOwed(TrackJob job) =>
        job.Status switch
        {
            JobStatus.Canceled or JobStatus.Queued or JobStatus.Running => true,
            JobStatus.Failed => !HasGivenUp(job),
            _ => false,
        };

    /// <summary>Failed for the last time: out of strikes, or failed in a way no retry can fix.</summary>
    public static bool HasGivenUp(TrackJob job) =>
        job is { Status: JobStatus.Failed }
        && (job.Strikes >= MaxStrikesFor(job.Stage) || IsPermanent(job, out _));

    /// <summary>Only a download can fail for good: YouTube will not serve that upload (#63).</summary>
    public static bool IsPermanent(TrackJob job, [NotNullWhen(true)] out string? reason)
    {
        reason = null;

        return job is { Status: JobStatus.Failed, Stage: JobStage.Download }
            && DownloadFailure.IsPermanent(job.Error, out reason);
    }

    public static bool Owes(JobStore jobs, string videoId, JobStage stage) =>
        jobs.TryGet(videoId, stage, out var job) && IsOwed(job);

    /// <param name="retrySucceeded">
    /// Whether a past success may be attempted again. True where the stage has its own artifact
    /// check that already confirmed the work is missing, so redoing it is cheap and correct.
    /// False for separation, where redoing it costs GPU hours the user may not want spent.
    /// </param>
    public static bool Allows(
        JobStore jobs,
        string videoId,
        JobStage stage,
        int maxStrikes,
        bool retrySucceeded
    ) =>
        !jobs.TryGet(videoId, stage, out var job)
        || job.Status switch
        {
            JobStatus.Succeeded => retrySucceeded,
            // Something is on it — or a previous crateside was, and it is being adopted.
            JobStatus.Queued or JobStatus.Running => false,
            // Canceling is the user's call, not the track's fault, so it is simply next again.
            JobStatus.Canceled => true,
            _ => job.Strikes < maxStrikes && !IsPermanent(job, out _),
        };
}
