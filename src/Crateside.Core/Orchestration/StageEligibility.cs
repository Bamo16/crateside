namespace Crateside.Core.Orchestration;

/// <summary>
/// Whether a stage may attempt a track again, given what crateside has already recorded running
/// for it.
/// </summary>
/// <remarks>
/// <para>
/// Extracted at its third caller rather than its second. The three stages agree on almost
/// everything — in-flight means leave it, cancelling is the user's call and not the track's
/// fault, and enough failures means stop — and differ on one thing, below.
/// </para>
/// <para>
/// This is the <em>scheduler's</em> policy and deliberately not the job store's: the store
/// counts strikes and says nothing about how many is too many.
/// </para>
/// </remarks>
public static class StageEligibility
{
    /// <param name="retrySucceeded">
    /// Whether a past success may be attempted again. True where the stage has its own artifact
    /// check that already said the work is missing — evidence checks the working set, a verdict
    /// checks the ledger — so a recorded success with no artifact means the artifact was lost,
    /// and redoing it is cheap and correct. False for separation, where the missing artifact is
    /// more likely to be stems the user deleted on purpose, and redoing it costs GPU hours
    /// against their wishes.
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
