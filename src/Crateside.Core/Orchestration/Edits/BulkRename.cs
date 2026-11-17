using System.Text.RegularExpressions;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Names many tracks at once from their tags, each through the song page's own rename, so stems,
/// the source copy and the Ableton sets follow and an open set holds a track back (#202).
/// </summary>
public sealed partial class BulkRename(TrackRename rename, ActionLog actions)
{
    public static IReadOnlyList<string> Presets { get; } =
    ["{album} - {title}", "{artist} - {title}", "{game} - {title}"];

    [GeneratedRegex(@"\{(?<Token>[^{}]*)\}")]
    private static partial Regex Placeholder { get; }

    /// <summary>What each row's files and sets are now, read once so every pattern is previewed from it.</summary>
    public IReadOnlyDictionary<string, RenameBasis> Prepare(IEnumerable<TrackRow> rows) =>
        Prepare([.. rows.Select(row => row.TrackKey).Distinct()]);

    /// <summary>What each track's files and sets are now, read once so every pattern is previewed from it.</summary>
    public IReadOnlyDictionary<string, RenameBasis> Prepare(IReadOnlyList<string> keys) =>
        rename.BasesOf(keys);

    /// <summary>The new name of every row, or why it gets none, with the files read now; nothing is changed.</summary>
    public IReadOnlyList<BulkRenameLine> PreviewChecked(IEnumerable<TrackRow> rows, string pattern)
    {
        var shown = rows.ToList();

        return Preview(shown, pattern, Prepare(shown));
    }

    /// <summary>
    /// The names the pattern gives from the tags alone, at once: no file is looked at, so each line is
    /// <see cref="BulkRenameLine.Pending"/> until the files have been read.
    /// </summary>
    public IReadOnlyList<BulkRenameLine> Preview(IEnumerable<TrackRow> rows, string pattern) =>
        Preview(rows, pattern, null);

    /// <summary>The new name of every row from a basis read earlier; a row without one is read now.</summary>
    public IReadOnlyList<BulkRenameLine> Preview(
        IEnumerable<TrackRow> rows,
        string pattern,
        IReadOnlyDictionary<string, RenameBasis>? bases
    )
    {
        var lines = rows.Select(row => LineFor(row, pattern, bases)).ToList();

        // Two tracks under one name would end up with files that clash.
        var taken = lines
            .Where(line => line.NewName is not null)
            .GroupBy(line => line.NewName!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. lines.Select(line =>
                line.NewName is { } name && taken.Contains(name)
                    ? line with
                    {
                        NewName = null,
                        Problem = $"another track in this batch would also be called {name}",
                    }
                    : line
            ),
        ];
    }

    /// <summary>Renames each line that has a new name; the rest are left as they are.</summary>
    public BulkRenameResult Apply(IEnumerable<BulkRenameLine> lines)
    {
        var renamed = 0;
        List<BulkRenameLine> refused = [];

        foreach (var line in lines.Where(line => line.WillRename))
        {
            var outcome = rename.Apply(line.TrackKey, line.NewName!);

            if (outcome.Succeeded)
            {
                actions.Record(line.TrackKey, UserActionKind.Renamed, line.NewName);
                renamed++;
            }
            else
                refused.Add(line with { NewName = null, Problem = outcome.Problem });
        }

        return new BulkRenameResult(renamed, refused);
    }

    /// <summary>The album before its colon: <c>Donkey Kong Country 2: Diddy's Kong Quest</c> is <c>Donkey Kong Country 2</c>.</summary>
    public static string? GameOf(string? album) =>
        album?.Split(':', 2)[0].Trim() is { Length: > 0 } game ? game : null;

    private BulkRenameLine LineFor(
        TrackRow row,
        string pattern,
        IReadOnlyDictionary<string, RenameBasis>? bases
    )
    {
        var missing = new List<string>();
        var unknown = new List<string>();
        var name = Placeholder
            .Replace(
                pattern,
                match =>
                {
                    var token = match.Groups["Token"].Value;
                    var value = ValueOf(row, token);

                    if (value is null && !IsToken(token))
                        unknown.Add(token);
                    else if (value is null)
                        missing.Add(token);

                    return value is null ? string.Empty : StemFileName.FileSafe(value);
                }
            )
            .Trim();
        var line = new BulkRenameLine(row.TrackKey, row.Title, null, null);

        if (unknown.Count > 0)
            return line with { Problem = $"{{{unknown[0]}}} is not a tag this can use" };

        if (missing.Count > 0)
            return line with { Problem = $"no {missing[0]} on this track" };

        if (name.Equals(row.Title, StringComparison.Ordinal))
            return line with { Problem = null, Unchanged = true };

        if (bases is null)
            return line with { NewName = name, Pending = true };

        var plan = bases.TryGetValue(row.TrackKey, out var basis)
            ? rename.PlanFor(basis, name)
            : rename.PlanFor(row.TrackKey, name);

        return plan.Refusal is { } refusal
            ? line with
            {
                Problem = refusal,
            }
            : line with
            {
                NewName = plan.Name,
            };
    }

    private static bool IsToken(string token) => token is "album" or "artist" or "title" or "game";

    private static string? ValueOf(TrackRow row, string token) =>
        token switch
        {
            "album" => row.Release?.Album,
            "game" => GameOf(row.Release?.Album),
            "artist" => row.TagArtist ?? row.ArtistAndTitle?.Artist,
            "title" => row.TagTitle ?? row.ArtistAndTitle?.Title,
            _ => null,
        }
            is { Length: > 0 } value
            ? value
            : null;
}

/// <summary>One track's row in a bulk rename preview.</summary>
public sealed record BulkRenameLine(
    string TrackKey,
    string Current,
    string? NewName,
    string? Problem
)
{
    /// <summary>The track already has the name the pattern gives it.</summary>
    public bool Unchanged { get; init; }

    /// <summary>The name comes from the tags alone; the files are not read yet, so a refusal may still come.</summary>
    public bool Pending { get; init; }

    public bool WillRename => NewName is not null;
}

/// <summary>How many tracks were renamed, and the lines that were refused at the time.</summary>
public sealed record BulkRenameResult(int Renamed, IReadOnlyList<BulkRenameLine> Refused);
