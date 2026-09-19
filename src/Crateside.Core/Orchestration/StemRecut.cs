using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// A track's files as they currently sit in <c>Sample Staging</c>.
/// </summary>
public delegate IReadOnlyList<StagingStem> ReadStagedFiles(string videoId);

/// <summary>
/// Changes which files a track has, without judging it again.
/// </summary>
/// <remarks>
/// Not always destructive. A plan that only adds removes nothing and leaves every staged file
/// where it is; one that only drops presets makes nothing and never touches the GPU (ADR 0018).
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
    /// reads cancelled as eligible again, whatever the strikes.
    /// </remarks>
    internal static readonly JobStage[] Redone =
    [
        JobStage.Separation,
        JobStage.Handoff,
        JobStage.Tagging,
    ];

    /// <summary>What changing this track's plan would do, or why it cannot be done.</summary>
    /// <param name="known">
    /// This track's files as the last queue scan found them, passed in rather than read here
    /// because scanning the library is the most expensive thing the app does. Only
    /// <paramref name="staged"/> is read fresh.
    /// </param>
    public RecutPlan PlanFor(
        string videoId,
        StemPlan wanted,
        PresetCatalog catalog,
        StemInventory? known = null
    )
    {
        var files = staged(videoId);
        var stagedInventory = StemInventory.Of(files.Select(AsOnDisk), catalog);

        // Everywhere the track's files could be, so a preset already sitting in Mashup Samples
        // is never asked for again — only staging is trusted to say what a re-cut may delete.
        var everywhere = new StemInventory
        {
            Files = [.. stagedInventory.Files, .. (known ?? StemInventory.Empty).Files],
        };

        var recipe = SeparationRecipe.For(wanted, everywhere);
        var removing = stagedInventory.Superseded(wanted, Stored(videoId));

        return new RecutPlan
        {
            Recipe = recipe,
            Removing = removing,
            WillRefetch = !workingSet.TryGetSource(videoId, out _),
            Refusal = RefusalFor(catalog, wanted, everywhere, recipe, removing),
        };
    }

    /// <summary>
    /// Removes what the plan dropped, records it, and puts the track back in line for whatever
    /// it now owes.
    /// </summary>
    /// <remarks>
    /// The order matters on failure: files are deleted first, and the stages are only reopened
    /// once every one of them went.
    /// </remarks>
    public RecutOutcome Apply(
        string videoId,
        StemPlan wanted,
        PresetCatalog catalog,
        StemInventory? known = null
    )
    {
        var plan = PlanFor(videoId, wanted, catalog, known);

        if (plan.Refusal is { } refusal)
            return new RecutOutcome { Problem = refusal };

        var removed = 0;

        foreach (var stem in plan.Removing)
        {
            try
            {
                RecycleBin.Remove(stem.File.Path);
                RecycleBin.Remove(StemFileName.AnalysisFor(stem.File.Path));

                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new RecutOutcome
                {
                    Removed = removed,
                    Problem =
                        $"{Path.GetFileName(stem.File.Path)} could not be removed — it is "
                        + "probably open in Ableton. Close it and try again.",
                };
            }
        }

        settings.SetPlan(videoId, wanted);

        // A change that only drops files needs no stage at all: nothing is made and nothing is
        // moved, so reopening separation would put the track in the line for a GPU that has
        // nothing to do for it.
        if (plan.MakesSomething)
            foreach (var stage in Redone)
                jobs.Record(videoId, stage, JobStatus.Cancelled, error: "re-cut requested");

        return new RecutOutcome { Removed = removed, Recipe = plan.Recipe };
    }

    /// <summary>The plan this track currently has, which says which of its files were added.</summary>
    private StemPlan Stored(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(track => track.VideoId.Equals(videoId, StringComparison.Ordinal))
            ?.Plan
        ?? StemPlan.Default;

    /// <summary>
    /// The staging board's view of a file as the scanner's, which is what the classifier reads.
    /// </summary>
    /// <remarks>
    /// A staged file's root is the folder it is in; only a scan across several roots has to be
    /// told which one it was looking at.
    /// </remarks>
    private static StemOnDisk AsOnDisk(StagingStem stem) =>
        new()
        {
            Path = stem.Path,
            Root = Path.GetDirectoryName(stem.Path) ?? string.Empty,
            Preset = stem.Preset,
            Label = stem.AdoptedAs,
            IsAdopted = stem.IsAdopted,
        };

    private static string? RefusalFor(
        PresetCatalog catalog,
        StemPlan wanted,
        StemInventory everywhere,
        SeparationRecipe recipe,
        IReadOnlyList<ClassifiedStem> removing
    ) =>
        (catalog, wanted, everywhere, removing) switch
        {
            // Without it nothing can say which of these files already exist, and cutting every
            // preset again would make a second copy of each under a suffixed name.
            ({ IsEmpty: true }, _, _, _) =>
                "The separator could not be asked what it can make, so crateside cannot tell "
                    + "which of these files this track already has.",
            (_, { IsEmpty: true }, _, _) => "A track has to keep something.",
            (_, _, { Files.Count: 0 }, _) =>
                "Nothing of this track is in Sample Staging or Mashup Samples.",
            _ when recipe is { Presets.Count: 0, KeepSource: false } && removing.Count is 0 =>
                "This track already has exactly these files.",
            _ => null,
        };
}

/// <summary>What changing a track's plan would do. Read before it happens, to say so on screen.</summary>
public sealed record RecutPlan
{
    public required SeparationRecipe Recipe { get; init; }

    /// <summary>The staged files the plan no longer asks for. Empty when it only adds.</summary>
    public required IReadOnlyList<ClassifiedStem> Removing { get; init; }

    /// <summary>
    /// The audio is no longer held, so separation downloads it again before it runs. Costs a
    /// fetch, not a verdict: the video ID is unchanged, so the ledger still answers for it.
    /// </summary>
    public required bool WillRefetch { get; init; }

    /// <summary>Nothing is lost — every file already staged stays where it is.</summary>
    public bool IsAdditive => Removing.Count is 0;

    /// <summary>Something new is made, so the pipeline has work to do.</summary>
    public bool MakesSomething => Recipe is { Presets.Count: > 0 } or { KeepSource: true };

    /// <summary>The sentence about the audio, said either way.</summary>
    /// <remarks>Only worth saying where something is going to be cut from it.</remarks>
    public string AudioDetail =>
        WillRefetch
            ? "The downloaded audio has gone, so separation fetches it again first."
            : "The downloaded audio is still on disk; nothing is re-downloaded.";

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
