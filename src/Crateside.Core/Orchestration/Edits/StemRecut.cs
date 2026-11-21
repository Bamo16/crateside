using Crateside.Core.Ableton;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Plans;
using Crateside.Core.Samples;
using Crateside.Core.SourceOffsets;
using Crateside.Core.Staging;
using Crateside.Core.Warps;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Changes which files a track has, without judging it again (ADR 0018).
/// </summary>
public sealed class StemRecut(
    TrackInstructionStore instructions,
    WorkingSet workingSet,
    IStagedFiles staged,
    TimeProvider time,
    DeletionLog? deletions = null,
    KeptWarps? keptWarps = null,
    ISetLinks? setLinks = null,
    SourceOffsetStore? offsets = null
)
{
    private readonly DeletionLog _deletions = deletions ?? DeletionLog.None;

    /// <summary>What changing this track's plan would do, or why it cannot be done.</summary>
    /// <param name="known">
    /// This track's files as the last queue scan found them, passed in rather than read here
    /// because scanning the library is the most expensive thing the app does. Only
    /// what is staged is read fresh.
    /// </param>
    public RecutPlan PlanFor(
        string trackKey,
        StemPlan wanted,
        PresetCatalog catalog,
        StemInventory? known = null
    )
    {
        var files = staged.Of(trackKey);
        var stagedInventory = StemInventory.Of(files.Select(AsOnDisk), catalog);

        // Everywhere the track's files could be: a preset already in the library is never asked
        // for again, and a library file the plan drops goes too, to the Recycle Bin (ADR 0103).
        var staging = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var everywhere = new StemInventory
        {
            Files =
            [
                .. stagedInventory.Files,
                .. (known ?? StemInventory.Empty).Files.Where(file =>
                    !staging.Contains(file.File.Path)
                ),
            ],
            IsFinished = known?.IsFinished is true,
        };

        var stored = Stored(trackKey);
        var recipe = SeparationRecipe.For(wanted, everywhere);
        var removing = everywhere.Superseded(wanted, stored);

        return new RecutPlan
        {
            Recipe = recipe,
            Removing = removing,
            Sets = SetsUsing(removing, staging),
            InLibrary = removing.Count(stem => !staging.Contains(stem.File.Path)),
            WillRefetch = !workingSet.TryGetSource(trackKey, out _),
            CutsGuideForAddedSources =
                recipe.ExtractDrums
                && wanted is { Presets.Count: 0, KeepSource: false, AddedSources.Count: > 0 },
            Refusal = RefusalFor(
                catalog,
                wanted,
                everywhere,
                recipe,
                removing,
                wanted.AsksTheSameAs(stored),
                AnyAligned(wanted)
            ),
        };
    }

    /// <summary>
    /// Removes what the plan dropped, records it, and only once every file has gone puts the
    /// track back in line for whatever it now owes.
    /// </summary>
    public RecutOutcome Apply(
        string trackKey,
        StemPlan wanted,
        PresetCatalog catalog,
        StemInventory? known = null
    )
    {
        var plan = PlanFor(trackKey, wanted, catalog, known);

        if (plan.Refusal is { } refusal)
            return new RecutOutcome { Problem = refusal };

        var removed = 0;

        // Kept before anything is recycled, so a restart during the cut cannot lose the warp.
        foreach (var stem in plan.Removing)
            keptWarps?.Keep(stem.File.Path);

        foreach (var stem in plan.Removing)
        {
            try
            {
                _deletions.Recycle(stem.File.Path, "dropped from the plan by a re-cut");
                RecycleBin.Remove(StemFileName.AnalysisFor(stem.File.Path));

                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

        instructions.SetPlan(trackKey, wanted);

        // A change that only drops files asks for nothing to be made.
        if (plan.MakesSomething)
            instructions.RequestCut(trackKey, time.GetUtcNow());

        return new RecutOutcome { Removed = removed, Recipe = plan.Recipe };
    }

    /// <summary>The Live sets that use a library file this re-cut would delete.</summary>
    private IReadOnlyList<string> SetsUsing(
        IReadOnlyList<ClassifiedStem> removing,
        HashSet<string> staging
    ) =>
        setLinks is null
            ? []
            : setLinks.Using([
                .. removing.Select(stem => stem.File.Path).Where(path => !staging.Contains(path)),
            ]);

    /// <summary>The plan this track currently has, which says which of its files were added.</summary>
    private StemPlan Stored(string trackKey) =>
        instructions
            .Read()
            .FirstOrDefault(track => track.TrackKey.Equals(trackKey, StringComparison.Ordinal))
            ?.Plan
        ?? StemPlan.Default;

    /// <summary>
    /// The staging reader's view of a file as the scanner's, which is what the classifier reads.
    /// </summary>
    private static StemOnDisk AsOnDisk(StagedFile stem) =>
        new()
        {
            Path = stem.Path,
            Root = Path.GetDirectoryName(stem.Path) ?? string.Empty,
            Preset = stem.Preset,
            Label = stem.AdoptedAs,
            IsAdopted = stem.IsAdopted,
        };

    private bool AnyAligned(StemPlan wanted) =>
        offsets is not null
        && wanted.AddedSources.Any(source =>
            offsets.Of(source.TrackKey)
                is { IsUsable: true, Reading.Verdict: OffsetVerdict.Aligned }
        );

    private static string? RefusalFor(
        PresetCatalog catalog,
        StemPlan wanted,
        StemInventory everywhere,
        SeparationRecipe recipe,
        IReadOnlyList<ClassifiedStem> removing,
        bool isStoredPlan,
        bool anyAddedAligned
    ) =>
        (catalog, wanted, everywhere, removing) switch
        {
            // Without it nothing can say which of these files already exist, and cutting every
            // preset again would make a second copy of each under a suffixed name.
            ({ IsEmpty: true }, _, _, _) =>
                "The separator could not be asked what it can make, so crateside cannot tell "
                    + "which of these files this track already has.",
            // Only an added source that lines up with the mix can carry the warp alone (ADR 0111, rule 24).
            (_, { IsEmpty: true, AddedSources.Count: > 0 }, _, _) when !anyAddedAligned =>
                "The official files must line up with the mix before the stems can go.",
            (_, { IsEmpty: true, AddedSources.Count: 0 }, _, _) => "A track has to keep something.",
            (_, _, { Files.Count: 0 }, _) => "Nothing of this track is in Staging or the library.",
            _ when recipe is { Presets.Count: 0, KeepSource: false }
                    && removing.Count is 0
                    && isStoredPlan => "This track already has exactly these files.",
            _ => null,
        };
}

/// <summary>What changing a track's plan would do. Read before it happens, to say so on screen.</summary>
public sealed record RecutPlan
{
    public required SeparationRecipe Recipe { get; init; }

    /// <summary>The staged and library files the plan no longer asks for. Empty when it only adds.</summary>
    public required IReadOnlyList<ClassifiedStem> Removing { get; init; }

    /// <summary>How many of the removed files are in the library, which go to the Recycle Bin.</summary>
    public int InLibrary { get; init; }

    /// <summary>The Live sets that use a library file being removed, named in the confirmation.</summary>
    public IReadOnlyList<string> Sets { get; init; } = [];

    /// <summary>
    /// The audio is no longer held, so separation downloads it again before it runs. Costs a
    /// fetch, not a verdict: the video ID is unchanged, so the ledger still answers for it.
    /// </summary>
    public required bool WillRefetch { get; init; }

    /// <summary>Nothing is lost — every file already staged stays where it is.</summary>
    public bool IsAdditive => Removing.Count is 0;

    /// <summary>Something new is made, so the pipeline has work to do.</summary>
    public bool MakesSomething =>
        Recipe is { Presets.Count: > 0 } or { KeepSource: true } || CutsGuideForAddedSources;

    /// <summary>The plan keeps only added sources and still owes the drums guide their markers are placed on (ADR 0111, rule 24).</summary>
    public bool CutsGuideForAddedSources { get; init; }

    /// <summary>The sentence about the audio, said either way.</summary>
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
