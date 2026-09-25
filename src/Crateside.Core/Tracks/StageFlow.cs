using Crateside.Core.Orchestration;
using Humanizer;

namespace Crateside.Core.Tracks;

/// <summary>
/// A track's current pass as the chips Track details draws under its band: one per stage, from
/// download to warp, with the time each finished (#21).
/// </summary>
public sealed record StageFlow(int Pass, string Reason, IReadOnlyList<FlowStep> Steps)
{
    /// <summary>The stages in the order a pass runs them, stage and tag drawn as one.</summary>
    private static readonly (string Label, JobStage[] Stages)[] Order =
    [
        ("Download", [JobStage.Download]),
        ("Detect", [JobStage.Evidence]),
        ("Judge", [JobStage.Verdict]),
        ("Separate", [JobStage.Separation]),
        ("Stage & tag", [JobStage.Handoff, JobStage.Tagging]),
    ];

    /// <param name="job">The job's current state at a stage, or null when it never ran.</param>
    /// <param name="actions">The user's actions on this track, oldest first.</param>
    public static StageFlow For(
        TrackRow row,
        Func<JobStage, TrackJob?> job,
        IReadOnlyList<UserAction> actions
    )
    {
        var starts = actions.Where(action => action.PassFrom is not null).ToList();
        var from = starts is [.., var last] ? last.PassFrom : null;

        List<FlowStep> steps =
        [
            .. Order.Select(step =>
                Step(
                    row,
                    step.Label,
                    [.. step.Stages.Select(job).OfType<TrackJob>()],
                    step.Stages,
                    from
                )
            ),
            Warp(row),
        ];

        return new(
            starts.Count + 1,
            starts is [.., var start] ? start.Kind.Humanize(LetterCasing.Sentence) : "First run",
            steps
        );
    }

    private static FlowStep Step(
        TrackRow row,
        string label,
        IReadOnlyList<TrackJob> jobs,
        JobStage[] stages,
        JobStage? from
    )
    {
        // A pass restarted later never runs the stages ahead of where it began.
        if (from is { } start && stages.Max() < start)
            return new(label, FlowState.Skipped);

        if (stages.Contains(JobStage.Separation) && row.Recipe.Passes is 0 && jobs.Count is 0)
            return new(label, FlowState.Skipped, Note: "nothing to separate");

        if (row.RunningStage is { } running && stages.Contains(running))
            return new(label, FlowState.Running, row.RunningSince);

        if (row.GaveUpAt is { } gaveUp && stages.Contains(gaveUp))
            return new(label, FlowState.GaveUp, Note: $"gave up ×{row.GaveUpStrikes}");

        if (Parked(row) is { } parkedAt && stages.Contains(parkedAt))
            return new(label, FlowState.Parked, Note: "parked");

        return jobs.Count == stages.Length && jobs.All(j => j.Status is JobStatus.Succeeded)
            ? new(label, FlowState.Done, jobs.Max(j => j.UpdatedAt))
            : new(label, FlowState.NotYet);
    }

    private static JobStage? Parked(TrackRow row) =>
        row.ParkedFor switch
        {
            ParkReason.PremiumShortfall => JobStage.Download,
            ParkReason.NoKey => JobStage.Verdict,
            _ => null,
        };

    private static FlowStep Warp(TrackRow row) =>
        row.State switch
        {
            TrackState.ReadyToWarp => new("Warp", FlowState.YourTurn),
            TrackState.Done => new("Warp", FlowState.Done),
            _ => new("Warp", FlowState.NotYet),
        };
}

public sealed record FlowStep(
    string Label,
    FlowState State,
    DateTimeOffset? At = null,
    string? Note = null
);

public enum FlowState
{
    Done,
    Running,
    YourTurn,
    NotYet,
    Skipped,
    Parked,
    GaveUp,
}
