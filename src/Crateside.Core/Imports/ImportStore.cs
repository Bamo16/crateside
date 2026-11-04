using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Imports;

/// <summary>
/// The audio of tracks that are not on YouTube, kept as <c>imports\&lt;key&gt;\&lt;original name&gt;</c> and
/// never pruned (ADR 0077).
/// </summary>
public sealed class ImportStore(CratesidePaths paths)
{
    private readonly string _root = paths.ImportsFolder;

    /// <summary>Every key holding an import.</summary>
    public IReadOnlyList<string> TrackKeys =>
        Directory.Exists(_root)
            ? [.. Directory.EnumerateDirectories(_root).Select(Path.GetFileName).OfType<string>()]
            : [];

    public long TotalBytes =>
        Directory.Exists(_root)
            ? Directory
                .EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length)
            : 0;

    public bool TryGetFile(string trackKey, [NotNullWhen(true)] out string? path) =>
        TryFind(_root, trackKey, out path);

    /// <summary>Copies or moves a file in as <paramref name="trackKey"/>'s audio, and says where it is now.</summary>
    public string Take(string trackKey, string sourcePath, bool move)
    {
        var folder = Path.Combine(_root, trackKey);
        var destination = Path.Combine(folder, Path.GetFileName(sourcePath));

        Directory.CreateDirectory(folder);

        if (move)
            File.Move(sourcePath, destination, overwrite: true);
        else
            File.Copy(sourcePath, destination, overwrite: true);

        return destination;
    }

    internal static bool TryFind(
        string importsFolder,
        string trackKey,
        [NotNullWhen(true)] out string? path
    )
    {
        var folder = Path.Combine(importsFolder, trackKey);

        path = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).FirstOrDefault() : null;

        return path is not null;
    }
}
