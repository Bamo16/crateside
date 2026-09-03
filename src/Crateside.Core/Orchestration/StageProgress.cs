namespace Crateside.Core.Orchestration;

/// <summary>
/// What crateside has recorded about one track, reduced to the two facts a row needs.
/// </summary>
/// <remarks>
/// The job store answers per stage; a row asks about a track. Reducing it in one place is what
/// lets every stage be checked — <c>Queue.razor</c> assembled the same answer over four of the
/// five and left tagging out, so a track that gave up while tagging said nothing at all.
/// </remarks>
public sealed record StageProgress
{
    /// <summary>The stage crateside believes has this track in flight, if any.</summary>
    public JobStage? Running { get; init; }

    /// <summary>The stage that ran out of strikes, if one has.</summary>
    public JobStage? GaveUpAt { get; init; }

    /// <summary>How many times <see cref="GaveUpAt"/> failed before it stopped trying.</summary>
    public int GaveUpStrikes { get; init; }

    public static StageProgress For(JobStore jobs, string videoId)
    {
        JobStage? running = null;
        JobStage? gaveUpAt = null;
        var gaveUpStrikes = 0;

        foreach (var stage in Stages)
        {
            if (!jobs.TryGet(videoId, stage, out var job))
                continue;

            if (job.Status is JobStatus.Running)
                running = stage;

            // Cancelled is not counted, whatever the strikes say: every selector reads it as
            // "eligible again", so a track put back in line by hand has not given up.
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
            GaveUpAt = gaveUpAt,
            GaveUpStrikes = gaveUpStrikes,
        };
    }

    /// <summary>
    /// How many failures each stage tolerates, read from the selectors that own the numbers
    /// rather than copied beside them.
    /// </summary>
    /// <remarks>
    /// Evidence is allowed more than the rest because acquiring audio fails for transient reasons
    /// the other stages do not share. This reads each selector's <em>default</em>; one constructed
    /// with a different limit would disagree, which nothing in the app does today.
    /// </remarks>
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
