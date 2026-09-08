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

    public static StageProgress For(JobStore jobs, string videoId)
    {
        JobStage? running = null;
        DateTimeOffset? runningSince = null;
        JobStage? gaveUpAt = null;
        var gaveUpStrikes = 0;

        foreach (var stage in Stages)
        {
            if (!jobs.TryGet(videoId, stage, out var job))
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
                && strikes >= Limit(stage)
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

    /// <summary>
    /// How many failures each stage tolerates. Reads each selector's <em>default</em>, so one
    /// constructed with a different limit would disagree.
    /// </summary>
    private static int Limit(JobStage stage) =>
        stage switch
        {
            JobStage.Evidence => EvidenceSelector.DefaultMaxStrikes,
            JobStage.Verdict => VerdictSelector.DefaultMaxStrikes,
            JobStage.Separation => SeparationSelector.DefaultMaxStrikes,
            JobStage.Handoff => HandoffSelector.DefaultMaxStrikes,
            JobStage.Tagging => TaggingSelector.DefaultMaxStrikes,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };

    private static readonly JobStage[] Stages = Enum.GetValues<JobStage>();
}
