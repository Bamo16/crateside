using System.Diagnostics.CodeAnalysis;

namespace AbletonSampleData;

/// <summary>
/// The keyword index Ableton keeps for one root folder, from its XMP or Ableton's own database.
/// Key it on the root folder, never on the XMP's per-install UUID.
/// </summary>
public sealed class FolderInfo
{
    private readonly Dictionary<string, FileTags> _byRelativePath;

    public FolderInfo(string rootFolder, IReadOnlyList<FileTags> entries)
    {
        RootFolder = rootFolder;
        Entries = entries;

        _byRelativePath = entries
            .GroupBy(e => Normalize(e.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public string RootFolder { get; }

    /// <summary>Every entry, in document order.</summary>
    public IReadOnlyList<FileTags> Entries { get; }

    /// <summary>What an unreadable or absent index yields.</summary>
    public static FolderInfo Empty(string rootFolder) => new(rootFolder, []);

    /// <summary>
    /// Looks up tags by path relative to <see cref="RootFolder"/>, or by absolute path under
    /// it. Returns false when the file carries no keywords.
    /// </summary>
    public bool TryGet(string path, [NotNullWhen(true)] out FileTags? tags)
    {
        var relative = Path.IsPathRooted(path) ? Path.GetRelativePath(RootFolder, path) : path;

        return _byRelativePath.TryGetValue(Normalize(relative), out tags);
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').Trim('/');
}

/// <summary>The keywords Live holds for a single file.</summary>
public sealed record FileTags
{
    /// <summary>Path relative to the root folder, as Live spells it.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Every keyword on the file, raw, as <c>Category|Value</c>.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>
    /// Keywords the user took off this file, never to be written back. Only the XMP carries them;
    /// read from the index, always empty.
    /// </summary>
    public IReadOnlyList<string> HiddenKeywords { get; init; } = [];
}
