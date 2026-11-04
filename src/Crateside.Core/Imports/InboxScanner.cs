using System.IO.Compression;

namespace Crateside.Core.Imports;

/// <summary>
/// Consumes <see cref="CratesidePaths.Inbox"/>: each audio file whose size has held still is
/// moved into the imports as a track, and a zip is extracted and deleted (ADR 0077).
/// </summary>
public sealed class InboxScanner(TrackIntake intake, CratesidePaths paths, TimeProvider time)
{
    /// <summary>How long a file's size must hold still before it is read.</summary>
    public static readonly TimeSpan StillFor = TimeSpan.FromSeconds(5);

    private static readonly string[] AudioExtensions =
    [
        ".flac",
        ".mp3",
        ".m4a",
        ".ogg",
        ".opus",
        ".wav",
        ".aif",
        ".aiff",
    ];

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
        var now = time.GetUtcNow();

        foreach (
            var file in Directory.EnumerateFiles(paths.Inbox, "*", SearchOption.AllDirectories)
        )
        {
            var extension = Path.GetExtension(file);

            if (PartialExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                continue;

            var isZip = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);

            if (!isZip && !AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
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
        }

        return new InboxScan(taken, left);
    }

    /// <summary>The recipe an inbox subfolder's name asks for; null for the inbox itself or any other name.</summary>
    internal Recipe? RecipeOf(string file) =>
        Path.GetRelativePath(paths.Inbox, file).Split(Path.DirectorySeparatorChar)
            is [var folder, _, ..]
        && Recipes.TryParse(folder, out var recipe)
            ? recipe
            : null;

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

    private List<IntakeOutcome> TakeInZip(string zip)
    {
        var recipe = RecipeOf(zip);
        var folder = Path.Combine(paths.ImportsFolder, ".incoming", Guid.NewGuid().ToString("n"));
        List<IntakeOutcome> outcomes = [];

        try
        {
            Directory.CreateDirectory(folder);

            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries.Where(IsAudio))
                    entry.ExtractToFile(Path.Combine(folder, Unique(folder, entry.Name)));
            }

            foreach (var file in Directory.EnumerateFiles(folder))
                outcomes.Add(intake.TakeInFile(file, IntakeMode.Move, recipe));

            File.Delete(zip);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _unreadable.Add(zip);
            outcomes.Add(
                new IntakeOutcome.Refused(
                    $"{Path.GetFileName(zip)} could not be read: {ex.Message}"
                )
            );
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return outcomes;
    }

    private static bool IsAudio(ZipArchiveEntry entry) =>
        entry.Name.Length > 0
        && AudioExtensions.Contains(
            Path.GetExtension(entry.Name),
            StringComparer.OrdinalIgnoreCase
        );

    // Two entries of one name, from different folders of the zip, are two tracks.
    private static string Unique(string folder, string name) =>
        File.Exists(Path.Combine(folder, name))
            ? $"{Path.GetFileNameWithoutExtension(name)}~{Guid.NewGuid():n}{Path.GetExtension(name)}"
            : name;
}

/// <summary>What one pass of the inbox did, and the files it left where they were.</summary>
public sealed record InboxScan(IReadOnlyList<IntakeOutcome> Outcomes, IReadOnlyList<string> Left);
