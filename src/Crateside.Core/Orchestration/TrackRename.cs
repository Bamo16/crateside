using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Puts the user's own name on a track, and on the files it already has.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The outputs are renamed, never the source audio.</strong> StemForge names stems from
/// artist and title as <em>separate</em> tags, so writing one name back to the download would
/// mean splitting it — and <c>" - "</c> is not a safe split when titles contain it. Renaming what
/// comes out of separation is one path for a track at any point in the pipeline: the files that
/// exist are renamed now, and the ones that do not are named at staging from the same stored
/// instruction.
/// </para>
/// <para>
/// <strong><c>Sample Staging</c> only.</strong> A file that has reached <c>Mashup Samples</c> is
/// the user's, renamed in Ableton along with everything else the move stands for (ADR 0007), and
/// the tag store there is not crateside's to write (ADR 0003).
/// </para>
/// </remarks>
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

        // Only the files whose name would actually change: one already carrying the name is not
        // work, and counting it would put a number in the button that nothing answers to.
        var moves = staged(videoId)
            .Select(stem => new { Stem = stem, To = StemFileName.Renamed(stem.Path, wanted) })
            .Where(move => !PathsMatch(move.Stem.Path, move.To))
            .ToArray();

        // Checked before anything moves, exactly as handoff does: a collision half way through
        // leaves a track under two names, which is worse than not starting.
        if (moves.FirstOrDefault(move => File.Exists(move.To)) is { } clash)
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
    /// <remarks>
    /// The derived name is passed in rather than worked out here, because deriving it is the
    /// page's join across the ledger, the staging board and the playlist — not this class's.
    /// </remarks>
    public RenameOutcome Revert(string videoId, string derivedName) =>
        Rename(videoId, derivedName, stored: null);

    /// <summary>
    /// Stores the instruction first, then moves the files.
    /// </summary>
    /// <remarks>
    /// That order on purpose. The name is the user's statement and is true whether or not every
    /// file caught up; a file Ableton has open stops the moves half way, and pressing again
    /// finishes them because the ones already renamed are no longer work.
    /// </remarks>
    private RenameOutcome Rename(string videoId, string name, string? stored)
    {
        var plan = PlanFor(videoId, name);

        if (plan.Refusal is { } refusal)
            return new RenameOutcome { Problem = refusal };

        settings.SetName(videoId, stored);

        List<TagRename> done = [];

        foreach (var stem in plan.Renaming)
        {
            var to = StemFileName.Renamed(stem.Path, plan.Name);

            try
            {
                File.Move(stem.Path, to);
                done.Add(Entry(stem.Path, to));
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
            TagNote = MoveTags(done),
        };
    }

    /// <summary>
    /// Carries the Ableton keywords across to the new names.
    /// </summary>
    /// <remarks>
    /// Second, and reported rather than fatal. A file renamed out from under its entry reads as
    /// untagged, which is the one state the tagging stage already puts right by itself.
    /// </remarks>
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
    public required IReadOnlyList<StagingStem> Renaming { get; init; }

    /// <summary>Why this cannot happen, or null when it can.</summary>
    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null;
}

/// <summary>What a rename actually did.</summary>
public sealed record RenameOutcome
{
    /// <summary>The name now recorded, or null when nothing was changed.</summary>
    public string? Name { get; init; }

    /// <summary>How many files were renamed.</summary>
    public int Renamed { get; init; }

    /// <summary>What went wrong, or null when nothing did.</summary>
    public string? Problem { get; init; }

    /// <summary>
    /// The tag store could not be updated, though the files were renamed. Worth saying and not
    /// worth stopping for: the files read as untagged and the tagging stage tags them again.
    /// </summary>
    public string? TagNote { get; init; }

    public bool Succeeded => Problem is null;
}
