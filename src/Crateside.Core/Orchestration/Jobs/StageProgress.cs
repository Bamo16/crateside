namespace Crateside.Core.Orchestration.Jobs;

/// <summary>What crateside has recorded about one track, reduced to the facts a row needs.</summary>
public sealed record StageProgress
{
    public JobStage? Running { get; init; }

    /// <summary>
    /// When the process started, for a separation, even one adopted at a restart; otherwise the
    /// job's last update, which for an open job is its start.
    /// </summary>
    public DateTimeOffset? RunningSince { get; init; }

    public JobStage? GaveUpAt { get; init; }

    public int GaveUpStrikes { get; init; }

    /// <summary>Why no retry can help, where that is known; null when the strikes simply ran out.</summary>
    public string? GaveUpFor { get; init; }

    /// <summary>A stage whose latest attempt failed with strikes left, so it is owed another.</summary>
    public StageFailure? Retrying { get; init; }

    /// <summary>The verdict stage has left the key to the person (ADR 0076).</summary>
    public bool AwaitingPerson { get; init; }

    public static StageProgress For(JobStore jobs, string videoId) => For(jobs, [videoId]);

    /// <summary>
    /// The same, across every upload one song has been cut from (ADR 0014): the latest record of
    /// a stage is that stage's state, whichever video ID carries it.
    /// </summary>
    public static StageProgress For(JobStore jobs, IEnumerable<string> videoIds)
    {
        var ids = videoIds.ToArray();

        JobStage? running = null;
        DateTimeOffset? runningSince = null;
        JobStage? gaveUpAt = null;
        var gaveUpStrikes = 0;
        string? gaveUpFor = null;
        StageFailure? retrying = null;
        var awaitingPerson = false;

        foreach (var stage in Stages)
        {
            if (Latest(jobs, ids, stage) is not { } job)
                continue;

            if (job is { Stage: JobStage.Verdict, Status: JobStatus.AwaitingPerson })
                awaitingPerson = true;

            if (job.Status is JobStatus.Running)
            {
                running = stage;
                runningSince = job.ProcessStartedAt ?? job.UpdatedAt;
            }

            if (
                job is { Status: JobStatus.Failed, Strikes: var failures }
                && StageEligibility.IsOwed(job)
            )
                retrying = new StageFailure(stage, failures, job.Error);

            // Canceled never counts, whatever the strikes say — a track put back in line by
            // hand has not given up.
            if (StageEligibility.HasGivenUp(job))
            {
                gaveUpAt = stage;
                gaveUpStrikes = job.Strikes;
                gaveUpFor = StageEligibility.IsPermanent(job, out var why) ? why : null;
            }
        }

        return new StageProgress
        {
            Running = running,
            RunningSince = runningSince,
            GaveUpAt = gaveUpAt,
            GaveUpStrikes = gaveUpStrikes,
            GaveUpFor = gaveUpFor,
            Retrying = retrying,
            AwaitingPerson = awaitingPerson,
        };
    }

    private static TrackJob? Latest(
        JobStore jobs,
        IReadOnlyList<string> videoIds,
        JobStage stage
    ) =>
        videoIds
            .Select(videoId => jobs.TryGet(videoId, stage, out var job) ? job : null)
            .OfType<TrackJob>()
            .MaxBy(job => job.UpdatedAt);

    /// <summary>
    /// How many failures each stage tolerates. Reads each selector's <em>default</em>, so one
    /// constructed with a different limit would disagree.
    /// </summary>
    private static readonly JobStage[] Stages = Enum.GetValues<JobStage>();
}

/// <summary>A stage that failed and will be tried again: its strikes so far and its last error.</summary>
public sealed record StageFailure(JobStage Stage, int Strikes, string? Error);
