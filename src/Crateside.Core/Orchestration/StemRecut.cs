using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// A track's files as they currently sit in <c>Sample Staging</c>.
/// </summary>
/// <remarks>
/// A read rather than the staging board itself, for the reason <see cref="Tracks.TracksService"/>
/// takes one: the board opens every audio file under the root with TagLib, and a re-cut has no
/// business depending on that in order to be understood or tested.
/// </remarks>
public delegate IReadOnlyList<Staging.StagingStem> ReadStagedFiles(string videoId);

/// <summary>
/// Cuts a track's stems again, to a different recipe, without judging it again.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The case this exists for is a fact discovered late.</strong> A release is marked as
/// having no vocals when it is queued, which derives a recipe of drums-only plus a kept source —
/// and the mistake only becomes visible during warping, weeks later, when the vocals turn out to
/// be there after all. Before this the track was simply stuck: nothing crateside ran would
/// separate it again.
/// </para>
/// <para>
/// <strong>Key detection cannot re-run, and not because anything here prevents it.</strong>
/// Deleting the staged files leaves the track with a verdict and no stems, which is the exact
/// test for <see cref="QueueView.AwaitingSeparation"/> — and that list is subtracted from
/// <see cref="QueueView.Outstanding"/>, which is the only list
/// <see cref="EvidenceSelector"/> and <see cref="VerdictSelector"/> read. The guard is the shape
/// of the work lists rather than a rule anyone has to remember, which is the only kind worth
/// having when the thing being guarded costs quota.
/// </para>
/// <para>
/// <strong>keytag's ledger is left alone.</strong> The verdict line records
/// <c>has_vocals</c> as it was believed at the time, and it is append-only and not crateside's
/// to revise. The instruction is crateside's own and is what the recipe derives from, so
/// correcting it here is enough to change what gets made (ADR 0004).
/// </para>
/// </remarks>
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
    /// reads cancelled as "eligible again, whatever the strikes", and the history should still
    /// show that a separation succeeded once and was undone on purpose.
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

        // Asked of the working set rather than inferred from staging. The janitor keeps a track's
        // audio while anything of it is still staged, so the inference is usually right — but the
        // working set is cache and is safe to delete by hand, so the only honest way to know the
        // audio is there is to look for it.
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
    /// The order matters on failure. Files are deleted first and the stages are only reopened if
    /// every one of them went: a track whose staging folder is half-empty and whose separation is
    /// eligible again would be re-cut into a folder still holding some of the old files. A file
    /// that will not delete is nearly always one Ableton has open, so the user closes it and
    /// presses again — by which point the deletions that did work have simply already happened.
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

        // Only now, and in this order: the fact decides the recipe, and the recipe is read when
        // the separation stage picks the track up.
        settings.SetInstrumentalRelease(videoId, isInstrumentalRelease);

        foreach (var stage in Redone)
            jobs.Record(videoId, stage, JobStatus.Cancelled, error: "re-cut requested");

        return new RecutOutcome { Removed = removed, Recipe = plan.Recipe };
    }
}

/// <summary>What a re-cut would do. Read before it happens, to say so on screen.</summary>
public sealed record RecutPlan
{
    /// <summary>What separation would be asked for instead.</summary>
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
