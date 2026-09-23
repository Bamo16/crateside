namespace Crateside.Core.Orchestration;

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
            // Cancelling is the user's call, not the track's fault, so it is simply next again.
            JobStatus.Cancelled => true,
            _ => job.Strikes < maxStrikes,
        };
}
