using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// A track's files as they currently sit in <c>Sample Staging</c>.
/// </summary>
public delegate IReadOnlyList<StagingStem> ReadStagedFiles(string videoId);

/// <summary>
/// Cuts a track's stems again, to a different recipe, without judging it again.
/// </summary>
public sealed class StemRecut(
    QueueSettingsStore settings,
    JobStore jobs,
    WorkingSet workingSet,
    ReadStagedFiles staged
)
{
    /// <summary>
    /// The stages that run again. Not evidence and not the verdict — that is the whole point.
    /// </summary>
    /// <remarks>
    /// Recorded as <see cref="JobStatus.Cancelled"/> rather than cleared: every selector already
    /// reads cancelled as eligible again, whatever the strikes.
    /// </remarks>
    private static readonly JobStage[] Redone =
    [
        JobStage.Separation,
        JobStage.Handoff,
        JobStage.Tagging,
    ];

    /// <summary>What re-cutting this track would do, or why it cannot be done.</summary>
    public RecutPlan PlanFor(string videoId, bool isInstrumentalRelease)
    {
        var recipe = SeparationRecipe.For(isInstrumentalRelease);

        var files = staged(videoId);

        return new RecutPlan
        {
            Recipe = recipe,
            Removing = files,
            Refusal = (files, workingSet.TryGetSource(videoId, out _)) switch
            {
                ({ Count: 0 }, _) => "Nothing of this track is in Sample Staging.",
                (_, false) =>
                    "The downloaded audio is no longer on disk, so this would have to fetch it again.",
                _ => null,
            },
        };
    }

    /// <summary>
    /// Records the corrected fact, clears the staged files, and puts the track back in line.
    /// </summary>
    /// <remarks>
    /// The order matters on failure: files are deleted first, and the stages are only reopened
    /// once every one of them went.
    /// </remarks>
    public RecutOutcome Apply(string videoId, bool isInstrumentalRelease)
    {
        var plan = PlanFor(videoId, isInstrumentalRelease);

        if (plan.Refusal is { } refusal)
            return new RecutOutcome { Problem = refusal };

        var removed = 0;

        foreach (var stem in plan.Removing)
        {
            try
            {
                File.Delete(stem.Path);
                File.Delete(StemFileName.AnalysisFor(stem.Path));

                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new RecutOutcome
                {
                    Removed = removed,
                    Problem =
                        $"{stem.FileName} could not be removed — it is probably open in Ableton. "
                        + "Close it and try again.",
                };
            }
        }

        settings.SetInstrumentalRelease(videoId, isInstrumentalRelease);

        foreach (var stage in Redone)
            jobs.Record(videoId, stage, JobStatus.Cancelled, error: "re-cut requested");

        return new RecutOutcome { Removed = removed, Recipe = plan.Recipe };
    }
}

/// <summary>What a re-cut would do. Read before it happens, to say so on screen.</summary>
public sealed record RecutPlan
{
    public required SeparationRecipe Recipe { get; init; }

    /// <summary>The staged files that would go — every one of them, warp guide included.</summary>
    public required IReadOnlyList<StagingStem> Removing { get; init; }

    /// <summary>Why this cannot happen, or null when it can.</summary>
    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null;
}

/// <summary>What a re-cut actually did.</summary>
public sealed record RecutOutcome
{
    public int Removed { get; init; }

    /// <summary>The recipe now recorded, or null when nothing was changed.</summary>
    public SeparationRecipe? Recipe { get; init; }

    /// <summary>What went wrong, or null when nothing did.</summary>
    public string? Problem { get; init; }

    public bool Succeeded => Problem is null;
}
