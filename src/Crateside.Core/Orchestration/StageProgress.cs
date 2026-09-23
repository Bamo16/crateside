namespace Crateside.Core.Orchestration;

/// <summary>What crateside has recorded about one track, reduced to the facts a row needs.</summary>
public sealed record StageProgress
{
    public JobStage? Running { get; init; }

    /// <summary>
    /// The job's last update. For an open job that is its start, since a stage records Running
    /// once and says nothing more until the work ends.
    /// </summary>
    public DateTimeOffset? RunningSince { get; init; }

    public JobStage? GaveUpAt { get; init; }

    public int GaveUpStrikes { get; init; }

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

        foreach (var stage in Stages)
        {
            if (Latest(jobs, ids, stage) is not { } job)
                continue;

            if (job.Status is JobStatus.Running)
            {
                running = stage;
                runningSince = job.UpdatedAt;
            }

            // Cancelled never counts, whatever the strikes say — a track put back in line by
            // hand has not given up.
            if (
                job is { Status: JobStatus.Failed, Strikes: var strikes }
                && strikes >= StageEligibility.MaxStrikesFor(stage)
            )
            {
                gaveUpAt = stage;
                gaveUpStrikes = strikes;
            }
        }

        return new StageProgress
        {
            Running = running,
            RunningSince = runningSince,
            GaveUpAt = gaveUpAt,
            GaveUpStrikes = gaveUpStrikes,
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
