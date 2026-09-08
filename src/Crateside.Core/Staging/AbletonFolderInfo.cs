using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Staging;

/// <summary>
/// The keyword index Ableton keeps for one root folder, read from its
/// <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c>.
/// </summary>
/// <remarks>
/// The UUID in the filename is per-install, not a folder identifier — key on the root
/// folder, never on that filename.
/// </remarks>
public sealed class AbletonFolderInfo
{
    private readonly Dictionary<string, AbletonFileTags> _byRelativePath;

    internal AbletonFolderInfo(string rootFolder, IReadOnlyList<AbletonFileTags> entries)
    {
        RootFolder = rootFolder;
        Entries = entries;

        _byRelativePath = entries
            .GroupBy(e => Normalize(e.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public string RootFolder { get; }

    /// <summary>Every entry, in document order.</summary>
    public IReadOnlyList<AbletonFileTags> Entries { get; }

    /// <summary>What an unreadable or absent XMP yields.</summary>
    public static AbletonFolderInfo Empty(string rootFolder) => new(rootFolder, []);

    /// <summary>
    /// Looks up tags by path relative to <see cref="RootFolder"/>, or by absolute path under
    /// it. Returns false when the file carries no keywords.
    /// </summary>
    public bool TryGet(string path, [NotNullWhen(true)] out AbletonFileTags? tags)
    {
        var relative = Path.IsPathRooted(path) ? Path.GetRelativePath(RootFolder, path) : path;

        return _byRelativePath.TryGetValue(Normalize(relative), out tags);
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').Trim('/');
}

/// <summary>The keywords Ableton holds for a single file.</summary>
public sealed record AbletonFileTags
{
    /// <summary>Path relative to the root folder, as Ableton spells it.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Every keyword on the file, raw, including ones crateside does not read.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>
    /// Recombined from the separate tonic and mode keywords, normalized to the ledger's ASCII
    /// spelling (<c>G# major</c>). Null when the file has no key, or only half of one — see
    /// <see cref="HasPartialKey"/>.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>The <c>Type|</c> keyword — Instrumental, Vocal, Loop.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// True when the file has a tonic but no mode, or a mode but no tonic. <see cref="Key"/>
    /// stays null in that case.
    /// </summary>
    public bool HasPartialKey { get; init; }

    /// <summary>
    /// Stricter than "has an entry": a file tagged only <c>Type|Vocal</c> has an entry and no
    /// key.
    /// </summary>
    public bool IsKeyTagged => Key is not null;
}
