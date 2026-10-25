using AbleKit.Sets;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Library;
using Crateside.Core.Samples;
using Crateside.Core.Staging;
using Crateside.Core.WorkLists;
using Humanizer;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Puts the user's own name on a track, on its files in <c>Staging</c> and
/// the library, and in the sets that use them (ADR 0039, ADR 0040).
/// </summary>
public sealed class TrackRename(
    TrackInstructionStore instructions,
    IStagedFiles staged,
    FolderInfoWriter writer,
    CratesidePaths paths,
    ILibraryFiles? library = null,
    ISetLinks? sets = null
)
{
    /// <summary>What follows the open set's name when it stops a rename.</summary>
    public const string OpenSetRefusal =
        "is open in Ableton and uses these files. Open another set, then rename.";

    /// <summary>What renaming this track to <paramref name="name"/> would do, or why it cannot.</summary>
    public RenamePlan PlanFor(string videoId, string? name)
    {
        var wanted = name?.Trim() ?? string.Empty;

        if (!StemFileName.IsUsable(wanted))
            return Impossible(wanted, @"A name cannot be blank or contain \ / : * ? "" < > |.");

        var moves = Files(videoId)
            .Select(file => new Move(file.Path, StemFileName.Renamed(file.Path, wanted), file.Root))
            .Where(move => !PathsMatch(move.From, move.To))
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
                $"{Path.GetFileName(clash.To)} is already in {Path.GetFileName(clash.Root)}."
            );

        if (
            moves.GroupBy(move => move.To, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)
        )
            return Impossible(
                wanted,
                "Two of these files would end up with the same name. They have no presets to tell them apart."
            );

        var check = Check([.. moves.Select(move => move.From)]);

        return new RenamePlan
        {
            Name = wanted,
            Renaming = check.Refusal is null ? [.. moves.Select(move => move.From)] : [],
            Sets = check.Sets,
            Refusal = check.Refusal,
        };
    }

    /// <summary>
    /// The user's sets that use any of this track's files, whatever the new name, and why the
    /// rename must wait if one of them is open.
    /// </summary>
    public SetCheck SetsUsing(string videoId) =>
        Check([.. Files(videoId).Select(file => file.Path)]);

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

        // A sourceless song is identified by its file name, so the files carry the name.
        if (!SourcelessSongs.IsKey(videoId))
            instructions.SetName(videoId, stored);

        List<Move> done = [];
        List<string> markersLost = [];

        foreach (var from in plan.Renaming)
        {
            var move = new Move(from, StemFileName.Renamed(from, plan.Name), RootOf(from));

            try
            {
                File.Move(move.From, move.To);
                done.Add(move);

                if (!TryMoveAnalysis(move.From, move.To))
                    markersLost.Add(Path.GetFileName(move.To));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MoveTags(done);

                return new RenameOutcome
                {
                    Name = plan.Name,
                    Renamed = done.Count,
                    Problem =
                        $"{Path.GetFileName(from)} could not be renamed — it is probably open in Ableton. "
                        + "Close it and try again.",
                };
            }
        }

        var relink = done.Count is 0
            ? null
            : sets?.Relink([.. done.Select(move => new SampleMove(move.From, move.To))]);

        return new RenameOutcome
        {
            Name = plan.Name,
            Renamed = done.Count,
            Note = Note(MoveTags(done), markersLost, relink?.Failed ?? []),
        };
    }

    private SetCheck Check(IReadOnlyCollection<string> files)
    {
        if (files.Count is 0 || sets is null)
            return new SetCheck([], null);

        var found = sets.Using(files);

        if (found.Count is 0)
            return new SetCheck(found, null);

        return sets.ReadOpenSet() switch
        {
            { Set: { } open } when found.Contains(open, StringComparer.OrdinalIgnoreCase) => new(
                found,
                $"{open} {OpenSetRefusal}",
                open
            ),
            { IsRunning: true, Set: null } => new(
                found,
                "Ableton is running, but its window does not say which set is open. "
                    + "Open a set or close Ableton, then rename."
            ),
            _ => new(found, null),
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>What did not follow the files, or null when everything did.</summary>
    private static string? Note(
        string? tags,
        List<string> markersLost,
        IReadOnlyList<string> setsLeft
    ) =>
        string.Join(
            " ",
            new[]
            {
                tags,
                markersLost.Count is 0
                    ? null
                    : $"Ableton's warp markers did not follow {"file".ToQuantity(markersLost.Count)}; "
                        + "they will be analyzed again.",
                setsLeft.Count is 0
                    ? null
                    : $"{string.Join(", ", setsLeft)} could not be updated; relink "
                        + $"{(setsLeft.Count is 1 ? "it" : "them")} in Ableton.",
            }.OfType<string>()
        )
            is { Length: > 0 } note
            ? note
            : null;

    /// <summary>Carries the Ableton keywords across to the new names, in each folder's store.</summary>
    private string? MoveTags(IReadOnlyList<Move> done) =>
        done.GroupBy(move => move.Root, StringComparer.OrdinalIgnoreCase)
            .Select(folder =>
                TagWriteOutcome.Of(() =>
                    writer.Rename(
                        folder.Key,
                        [
                            .. folder.Select(move => new TagRename
                            {
                                From = Path.GetRelativePath(folder.Key, move.From),
                                To = Path.GetRelativePath(folder.Key, move.To),
                            }),
                        ]
                    )
                ) switch
                {
                    TagWriteOutcome.Stale(var error) => error,
                    TagWriteOutcome.Rejected(var error) => error,
                    _ => null,
                }
            )
            .OfType<string>()
            .FirstOrDefault();

    private IEnumerable<(string Path, string Root)> Files(string videoId) =>
        [
            .. staged.Of(videoId).Select(stem => (stem.Path, paths.StagingFolder)),
            .. (library?.Of(videoId) ?? []).Select(path => (path, paths.LibraryFolder)),
        ];

    private string RootOf(string path) =>
        path.StartsWith(paths.LibraryFolder, StringComparison.OrdinalIgnoreCase)
            ? paths.LibraryFolder
            : paths.StagingFolder;

    private static RenamePlan Impossible(string name, string refusal) =>
        new()
        {
            Name = name,
            Renaming = [],
            Refusal = refusal,
        };

    private static bool PathsMatch(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private sealed record Move(string From, string To, string Root);
}

/// <summary>The sets a rename would update, and why it must wait, or null when it need not.</summary>
/// <param name="Blocking">The open set that uses the files, when that is why.</param>
public sealed record SetCheck(IReadOnlyList<string> Sets, string? Refusal, string? Blocking = null);

/// <summary>What a rename would do. Read before it happens, to say so on screen.</summary>
public sealed record RenamePlan
{
    /// <summary>The name as it would be stored — trimmed, and what the files would be called.</summary>
    public required string Name { get; init; }

    /// <summary>The files that would change name. Empty when the track has none yet.</summary>
    public required IReadOnlyList<string> Renaming { get; init; }

    /// <summary>The user's Ableton sets that use those files by path, to be relinked (ADR 0040).</summary>
    public IReadOnlyList<string> Sets { get; init; } = [];

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
