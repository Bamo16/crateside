namespace Crateside.Core.Imports;

/// <summary>
/// Consumes <see cref="CratesidePaths.Inbox"/>: each audio file whose size has held still is
/// moved into the imports as a track, and a zip is extracted and deleted (ADR 0077).
/// </summary>
public sealed class InboxScanner(TrackIntake intake, CratesidePaths paths, TimeProvider time)
{
    /// <summary>How long a file's size must hold still before it is read.</summary>
    public static readonly TimeSpan StillFor = TimeSpan.FromSeconds(5);

    private static readonly string[] PartialExtensions = [".part", ".crdownload", ".tmp"];

    private readonly Dictionary<string, (long Size, DateTimeOffset Since)> _seen = [];
    private readonly HashSet<string> _named = [];
    private readonly HashSet<string> _unreadable = [];

    public InboxScan Scan()
    {
        if (!Directory.Exists(paths.Inbox))
            return new InboxScan([], []);

        List<IntakeOutcome> taken = [];
        List<string> left = [];
        HashSet<string> consumedFrom = [];
        var now = time.GetUtcNow();

        foreach (
            var file in Directory.EnumerateFiles(paths.Inbox, "*", SearchOption.AllDirectories)
        )
        {
            var extension = Path.GetExtension(file);

            if (PartialExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                continue;

            var isZip = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);

            if (!isZip && !TrackIntake.IsAudioFile(file))
            {
                if (_named.Add(file))
                    left.Add(file);

                continue;
            }

            if (_unreadable.Contains(file))
                continue;

            if (!IsReady(file, now))
                continue;

            _seen.Remove(file);

            if (isZip)
                taken.AddRange(TakeInZip(file));
            else if (intake.TakeInFile(file, IntakeMode.Move, RecipeOf(file)) is { } outcome)
            {
                taken.Add(outcome);

                if (outcome is IntakeOutcome.Refused)
                    _unreadable.Add(file);
            }

            if (!File.Exists(file) && Path.GetDirectoryName(file) is { } folder)
                consumedFrom.Add(folder);
        }

        foreach (var folder in consumedFrom)
            RemoveEmptied(folder);

        return new InboxScan(taken, left);
    }

    /// <summary>The recipe an inbox subfolder's name asks for; null for the inbox itself or any other name.</summary>
    internal Recipe? RecipeOf(string file) =>
        Path.GetRelativePath(paths.Inbox, file).Split(Path.DirectorySeparatorChar)
            is [var folder, _, ..]
        && Recipes.TryParse(folder, out var recipe)
            ? recipe
            : null;

    /// <summary>Removes a folder the intake emptied and its emptied parents, never the inbox or a recipe folder.</summary>
    private void RemoveEmptied(string folder)
    {
        while (IsRemovable(folder))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(folder).Any())
                    return;

                Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            folder = Path.GetDirectoryName(folder)!;
        }
    }

    private bool IsRemovable(string folder) =>
        Path.GetRelativePath(paths.Inbox, folder).Split(Path.DirectorySeparatorChar) switch
        {
            ["."] or [".." or ".", ..] => false, // the inbox itself, or outside it
            [var top] => !Recipes.TryParse(top, out _),
            _ => true,
        };

    private bool IsReady(string file, DateTimeOffset now)
    {
        long size;

        try
        {
            size = new FileInfo(file).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (!_seen.TryGetValue(file, out var last) || last.Size != size)
        {
            _seen[file] = (size, now);

            return false;
        }

        return now - last.Since >= StillFor && CanOpenExclusively(file);
    }

    private static bool CanOpenExclusively(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private IReadOnlyList<IntakeOutcome> TakeInZip(string zip)
    {
        var outcomes = intake.TakeInZip(zip, RecipeOf(zip));

        if (outcomes is [IntakeOutcome.Refused])
            _unreadable.Add(zip);

        return outcomes;
    }
}

/// <summary>What one pass of the inbox did, and the files it left where they were.</summary>
public sealed record InboxScan(IReadOnlyList<IntakeOutcome> Outcomes, IReadOnlyList<string> Left);
