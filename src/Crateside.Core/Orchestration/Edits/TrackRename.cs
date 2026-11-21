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
    ISetLinks? sets = null,
    FolderInfoReader? reader = null
)
{
    private const int StaleRetries = 3;

    private static readonly TimeSpan StaleRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly FolderInfoReader _reader = reader ?? new FolderInfoReader();

    /// <summary>What follows the open set's name when it stops a rename.</summary>
    public const string OpenSetRefusal =
        "is open in Ableton and uses these files. Open another set, then rename.";

    /// <summary>What renaming this track to <paramref name="name"/> would do, or why it cannot.</summary>
    public RenamePlan PlanFor(string trackKey, string? name) => PlanFor(BasisOf(trackKey), name);

    /// <summary>
    /// What a track's files and the sets using them are now, read once so many names can be tried
    /// against it without reading again.
    /// </summary>
    public RenameBasis BasisOf(
        string trackKey,
        ExistingFiles? existing = null,
        bool noSetUsesAny = false
    )
    {
        var files = Files(trackKey).ToArray();

        return new RenameBasis(
            files,
            noSetUsesAny ? new SetCheck([], null) : Check([.. files.Select(file => file.Path)]),
            existing
        );
    }

    /// <summary>
    /// <see cref="BasisOf(string, ExistingFiles?, bool)"/> for many tracks, reading staging and the
    /// library once for all of them instead of once per track.
    /// </summary>
    public IReadOnlyDictionary<string, RenameBasis> BasesOf(IReadOnlyCollection<string> trackKeys)
    {
        ExistingFiles existing = new();
        var files = FilesOf(trackKeys);
        var anySet =
            sets is { } links
            && links
                .Using([.. files.Values.SelectMany(list => list).Select(file => file.Path)])
                .Count > 0;

        return trackKeys.ToDictionary(
            key => key,
            key => new RenameBasis(
                files[key],
                anySet ? Check([.. files[key].Select(file => file.Path)]) : new SetCheck([], null),
                existing
            )
        );
    }

    /// <summary>Whether any set uses a file of any of these tracks: one walk of the sets, not one per track.</summary>
    public bool AnySetUses(IEnumerable<string> trackKeys) =>
        sets is { } links
        && links.Using([.. trackKeys.SelectMany(Files).Select(file => file.Path)]).Count > 0;

    private static bool Exists(RenameBasis basis, string path) =>
        basis.Existing?.Contains(path) ?? File.Exists(path);

    /// <summary>
    /// <see cref="PlanFor(string, string?)"/> against a basis already read: only the files a name
    /// would collide with are looked at. The sets are those using any of the track's files.
    /// </summary>
    public RenamePlan PlanFor(RenameBasis basis, string? name)
    {
        var wanted = name?.Trim() ?? string.Empty;

        if (!StemFileName.IsUsable(wanted))
            return Impossible(wanted, @"A name cannot be blank or contain \ / : * ? "" < > |.");

        var moves = basis
            .Files.Select(file => new Move(
                file.Path,
                StemFileName.Renamed(file.Path, wanted),
                file.Root
            ))
            .Where(move => !PathsMatch(move.From, move.To))
            .ToArray();

        // The analysis file counts — overwriting one would replace somebody's warp markers.
        if (
            moves.FirstOrDefault(move =>
                Exists(basis, move.To) || Exists(basis, StemFileName.AnalysisFor(move.To))
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

        var check = moves.Length is 0 ? new SetCheck([], null) : basis.Check;

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
    public SetCheck SetsUsing(string trackKey) =>
        Check([.. Files(trackKey).Select(file => file.Path)]);

    /// <summary>Records the name and puts it on whatever files the track already has.</summary>
    public RenameOutcome Apply(string trackKey, string name) =>
        Rename(trackKey, name, stored: name);

    /// <summary>
    /// Drops the override and puts the derived name back on the files.
    /// </summary>
    public RenameOutcome Revert(string trackKey, string derivedName) =>
        Rename(trackKey, derivedName, stored: null);

    /// <summary>Why these files cannot take these names, or null when they can.</summary>
    public string? RefusalFor(IReadOnlyList<FileMove> moves) =>
        moves.FirstOrDefault(move =>
            !PathsMatch(move.From, move.To)
            && (File.Exists(move.To) || File.Exists(StemFileName.AnalysisFor(move.To)))
        )
            is { } clash
            ? $"{Path.GetFileName(clash.To)} is already in {Path.GetFileName(Path.GetDirectoryName(clash.To))}."
            : Check([.. moves.Select(move => move.From)]).Refusal;

    /// <summary>Gives files the names given, taking their warp markers, Ableton tags and sets with them.</summary>
    public RenameOutcome Rename(IReadOnlyList<FileMove> moves) =>
        RefusalFor(moves) is { } refusal
            ? new RenameOutcome { Problem = refusal }
            : Moved(
                [
                    .. moves
                        .Where(move => !PathsMatch(move.From, move.To))
                        .Select(move => new Move(move.From, move.To, RootOf(move.From))),
                ],
                name: null
            );

    /// <summary>
    /// Stores the instruction, then moves the files — in that order, so a rename stopped half way
    /// is finished by pressing again.
    /// </summary>
    private RenameOutcome Rename(string trackKey, string name, string? stored)
    {
        var plan = PlanFor(trackKey, name);

        if (plan.Refusal is { } refusal)
            return new RenameOutcome { Problem = refusal };

        // A sourceless song is identified by its file name, so the files carry the name.
        if (!SourcelessSongs.IsKey(trackKey))
            instructions.SetName(trackKey, stored);

        return Moved(
            [
                .. plan.Renaming.Select(from => new Move(
                    from,
                    StemFileName.Renamed(from, plan.Name),
                    RootOf(from)
                )),
            ],
            plan.Name
        );
    }

    private RenameOutcome Moved(IReadOnlyList<Move> moves, string? name)
    {
        var held = Hold([.. moves.Select(move => move.From)]);
        List<Move> done = [];
        List<string> markersLost = [];

        foreach (var move in moves)
        {
            try
            {
                File.Move(move.From, move.To);
                done.Add(move);

                if (!TryMoveAnalysis(move.From, move.To))
                    markersLost.Add(Path.GetFileName(move.To));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MoveTags(done, held);

                return new RenameOutcome
                {
                    Name = name,
                    Renamed = done.Count,
                    Problem =
                        $"{Path.GetFileName(move.From)} could not be renamed — it is probably open in Ableton. "
                        + "Close it and try again.",
                };
            }
        }

        var relink = done.Count is 0
            ? null
            : sets?.Relink([.. done.Select(move => new SampleMove(move.From, move.To))]);

        return new RenameOutcome
        {
            Name = name,
            Renamed = done.Count,
            Note = Note(MoveTags(done, held), markersLost, relink?.Failed ?? []),
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

    /// <summary>
    /// Carries the Ableton keywords and Collections across to the new names, in each folder's
    /// store. An entry Live dropped as the files moved is written back from what it held before.
    /// </summary>
    private string? MoveTags(
        IReadOnlyList<Move> done,
        IReadOnlyDictionary<string, FileTags> held
    ) =>
        done.GroupBy(move => move.Root, StringComparer.OrdinalIgnoreCase)
            .Select(folder =>
                Failure(
                    Retrying(() =>
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
                    )
                ) ?? Restore(folder.Key, [.. folder], held)
            )
            .OfType<string>()
            .FirstOrDefault();

    private string? Restore(
        string root,
        IReadOnlyList<Move> moves,
        IReadOnlyDictionary<string, FileTags> held
    )
    {
        List<TagAssignment> lost = [];

        try
        {
            var store = _reader.Read(root);

            foreach (var move in moves)
                if (!store.TryGet(move.To, out _) && held.TryGetValue(move.From, out var tags))
                    lost.Add(
                        new TagAssignment
                        {
                            RelativePath = Path.GetRelativePath(root, move.To),
                            Keywords = tags.Keywords,
                            Collections = tags.Collections,
                        }
                    );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        return lost.Count is 0 ? null : Failure(Retrying(() => writer.Apply(root, lost)));
    }

    /// <summary>What each file's entry held before it moved, by its old path.</summary>
    private Dictionary<string, FileTags> Hold(IReadOnlyList<string> files)
    {
        Dictionary<string, FileTags> held = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (var root in files.GroupBy(RootOf, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var store = _reader.Read(root.Key);

                foreach (var file in root)
                    if (store.TryGet(file, out var tags))
                        held[file] = tags;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A store that cannot be read now cannot be restored from; the move still goes ahead.
            }
        }

        return held;
    }

    private static TagWriteOutcome Retrying(Func<int> write)
    {
        var outcome = TagWriteOutcome.Of(write);

        for (var i = 0; i < StaleRetries && outcome is TagWriteOutcome.Stale; i++)
        {
            Thread.Sleep(StaleRetryDelay);
            outcome = TagWriteOutcome.Of(write);
        }

        return outcome;
    }

    private static string? Failure(TagWriteOutcome outcome) =>
        outcome switch
        {
            TagWriteOutcome.Stale(var error) => error,
            TagWriteOutcome.Rejected(var error) => error,
            _ => null,
        };

    private IEnumerable<(string Path, string Root)> Files(string trackKey) =>
        [
            .. staged.Of(trackKey).Select(stem => (stem.Path, paths.StagingFolder)),
            .. (library?.Of(trackKey) ?? []).Select(path => (path, paths.LibraryFolder)),
        ];

    private Dictionary<string, (string Path, string Root)[]> FilesOf(
        IReadOnlyCollection<string> trackKeys
    )
    {
        var stagedFiles = staged.Of(trackKeys);
        var libraryFiles = library?.Of(trackKeys);

        return trackKeys.ToDictionary(
            key => key,
            key =>
                stagedFiles[key]
                    .Select(stem => (stem.Path, paths.StagingFolder))
                    .Concat((libraryFiles?[key] ?? []).Select(path => (path, paths.LibraryFolder)))
                    .ToArray()
        );
    }

    private string RootOf(string path) =>
        paths.LibraryFolder is { Length: > 0 } library
        && path.StartsWith(library, StringComparison.OrdinalIgnoreCase)
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

/// <summary>One file and the name it is to take.</summary>
public sealed record FileMove(string From, string To);

/// <summary>A track's files and the sets using them, as <see cref="TrackRename.BasisOf"/> read them.</summary>
/// <param name="Existing">What is on disk, listed once; null looks at the disk each time.</param>
public sealed record RenameBasis(
    IReadOnlyList<(string Path, string Root)> Files,
    SetCheck Check,
    ExistingFiles? Existing = null
);

/// <summary>Which files are in a set of folders, each folder listed once, on first use.</summary>
public sealed class ExistingFiles
{
    private readonly Dictionary<string, HashSet<string>> _folders =
    [
        with(StringComparer.OrdinalIgnoreCase),
    ];

    public bool Contains(string path)
    {
        if (Path.GetDirectoryName(path) is not { } folder)
            return File.Exists(path);

        if (!_folders.TryGetValue(folder, out var names))
        {
            try
            {
                names = Directory.Exists(folder)
                    ? [.. Directory.EnumerateFiles(folder).Select(file => Path.GetFileName(file))]
                    : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return File.Exists(path);
            }

            _folders[folder] = names;
        }

        return names.Contains(Path.GetFileName(path));
    }
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
