using Crateside.Core.Ableton;
using Crateside.Core.Queue;
using Crateside.Core.Samples;
using Crateside.Core.Staging;
using Humanizer;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Puts the user's own name on a track, and on the files it has in <c>Sample Staging</c>.
/// </summary>
public sealed class TrackRename(
    QueueSettingsStore settings,
    ReadStagedFiles staged,
    AbletonFolderInfoWriter writer,
    CratesidePaths paths
)
{
    /// <summary>What renaming this track to <paramref name="name"/> would do, or why it cannot.</summary>
    public RenamePlan PlanFor(string videoId, string? name)
    {
        var wanted = name?.Trim() ?? string.Empty;

        if (!StemFileName.IsUsable(wanted))
            return Impossible(wanted, @"A name cannot be blank or contain \ / : * ? "" < > |.");

        var moves = staged(videoId)
            .Select(stem => new { Stem = stem, To = StemFileName.Renamed(stem.Path, wanted) })
            .Where(move => !PathsMatch(move.Stem.Path, move.To))
            .ToArray();

        // The analysis file counts — overwriting one would replace somebody's warp markers.
        if (
            moves.FirstOrDefault(move =>
                File.Exists(move.To) || File.Exists(StemFileName.AnalysisFor(move.To))
            ) is
            { } clash
        )
            return Impossible(
                wanted,
                $"{Path.GetFileName(clash.To)} is already in Sample Staging."
            );

        if (
            moves.GroupBy(move => move.To, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)
        )
            return Impossible(
                wanted,
                "Two of these files would end up with the same name. They have no presets to tell them apart."
            );

        return new RenamePlan { Name = wanted, Renaming = [.. moves.Select(move => move.Stem)] };
    }

    /// <summary>Records the name and puts it on whatever files the track already has.</summary>
    public RenameOutcome Apply(string videoId, string name) => Rename(videoId, name, stored: name);

    /// <summary>
    /// Drops the override and puts the derived name back on the files.
    /// </summary>
    public RenameOutcome Revert(string videoId, string derivedName) =>
        Rename(videoId, derivedName, stored: null);

    /// <summary>
    /// Stores the instruction, then moves the files — in that order, so a rename stopped half way
    /// is finished by pressing again.
    /// </summary>
    private RenameOutcome Rename(string videoId, string name, string? stored)
    {
        var plan = PlanFor(videoId, name);

        if (plan.Refusal is { } refusal)
            return new RenameOutcome { Problem = refusal };

        settings.SetName(videoId, stored);

        List<TagRename> done = [];
        List<string> markersLost = [];

        foreach (var stem in plan.Renaming)
        {
            var to = StemFileName.Renamed(stem.Path, plan.Name);

            try
            {
                File.Move(stem.Path, to);
                done.Add(Entry(stem.Path, to));

                if (!TryMoveAnalysis(stem.Path, to))
                    markersLost.Add(Path.GetFileName(to));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MoveTags(done);

                return new RenameOutcome
                {
                    Name = plan.Name,
                    Renamed = done.Count,
                    Problem =
                        $"{stem.FileName} could not be renamed — it is probably open in Ableton. "
                        + "Close it and try again.",
                };
            }
        }

        return new RenameOutcome
        {
            Name = plan.Name,
            Renamed = done.Count,
            Note = Note(MoveTags(done), markersLost),
        };
    }

    /// <summary>
    /// Takes Ableton's analysis file with the sample, so the warp markers survive the rename.
    /// </summary>
    private static bool TryMoveAnalysis(string from, string to)
    {
        var sidecar = StemFileName.AnalysisFor(from);

        if (!File.Exists(sidecar))
            return true;

        try
        {
            File.Move(sidecar, StemFileName.AnalysisFor(to));

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>What did not follow the files, or null when everything did.</summary>
    private static string? Note(string? tags, IReadOnlyList<string> markersLost) =>
        (tags, markersLost) switch
        {
            (null, { Count: 0 }) => null,
            (null, _) =>
                $"Ableton's warp markers did not follow {"file".ToQuantity(markersLost.Count)}; "
                    + "they will be analysed again.",
            ({ } problem, { Count: 0 }) => problem,
            _ => $"{tags} Warp markers were also left behind for "
                + $"{"file".ToQuantity(markersLost.Count)}.",
        };

    /// <summary>Carries the Ableton keywords across to the new names.</summary>
    private string? MoveTags(IReadOnlyList<TagRename> done) =>
        done.Count is 0 ? null : writer.Rename(paths.SampleStaging, done).Error;

    private TagRename Entry(string from, string to) =>
        new()
        {
            From = Path.GetRelativePath(paths.SampleStaging, from),
            To = Path.GetRelativePath(paths.SampleStaging, to),
        };

    private static RenamePlan Impossible(string name, string refusal) =>
        new()
        {
            Name = name,
            Renaming = [],
            Refusal = refusal,
        };

    private static bool PathsMatch(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a rename would do. Read before it happens, to say so on screen.</summary>
public sealed record RenamePlan
{
    /// <summary>The name as it would be stored — trimmed, and what the files would be called.</summary>
    public required string Name { get; init; }

    /// <summary>The staged files that would change name. Empty when the track has none yet.</summary>
    public required IReadOnlyList<StagedFile> Renaming { get; init; }

    /// <summary>Why this cannot happen, or null when it can.</summary>
    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null;
}

/// <summary>What a rename actually did.</summary>
public sealed record RenameOutcome
{
    /// <summary>The name now recorded, or null when nothing was changed.</summary>
    public string? Name { get; init; }

    public int Renamed { get; init; }

    /// <summary>What went wrong, or null when nothing did.</summary>
    public string? Problem { get; init; }

    /// <summary>
    /// The tag store or the warp markers did not follow, though the files were renamed — which
    /// still <see cref="Succeeded"/>.
    /// </summary>
    public string? Note { get; init; }

    public bool Succeeded => Problem is null;
}
