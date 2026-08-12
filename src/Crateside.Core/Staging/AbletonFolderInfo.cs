using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Staging;

/// <summary>
/// The keyword index Ableton keeps for one root folder, read from its
/// <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c>.
/// </summary>
/// <remarks>
/// The UUID in the filename is per-user/per-install, not a folder identifier — it is
/// byte-identical across both of the user's roots. Key on the containing root folder,
/// never on that filename.
/// </remarks>
public sealed class AbletonFolderInfo
{
    private readonly Dictionary<string, AbletonFileTags> _byRelativePath;

    internal AbletonFolderInfo(string rootFolder, IReadOnlyList<AbletonFileTags> entries)
    {
        RootFolder = rootFolder;
        Entries = entries;

        // Ableton records paths relative to the root, so equal names under different roots
        // do not collide. Windows paths are case-insensitive; matching is too.
        _byRelativePath = entries
            .GroupBy(e => Normalize(e.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The root folder this index belongs to.</summary>
    public string RootFolder { get; }

    /// <summary>Every entry in the file, in document order.</summary>
    public IReadOnlyList<AbletonFileTags> Entries { get; }

    /// <summary>An empty index — what an unreadable or absent XMP yields.</summary>
    public static AbletonFolderInfo Empty(string rootFolder) => new(rootFolder, []);

    /// <summary>
    /// Looks up the tags for a file by its path relative to <see cref="RootFolder"/>, or by
    /// its absolute path under that root. Returns false for a file with no entry — which is
    /// the normal state for a file that carries no keywords at all.
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
    /// The key, recombined from the separate tonic and mode keywords and normalized to the
    /// ledger's ASCII spelling (<c>G# major</c>). Null when the file carries no key, or
    /// carries only half of one — see <see cref="HasPartialKey"/>.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>The <c>Type|</c> keyword — Instrumental, Vocal, Loop.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// True when the file has a tonic but no mode, or a mode but no tonic. Real data contains
    /// this (two files tagged <c>Key|D</c> with no <c>Key|Major</c>/<c>Key|Minor</c>), and
    /// half a key is not a key: <see cref="Key"/> stays null so nothing downstream pairs on
    /// it, but the flag lets the board show "half-tagged" rather than silently "untagged".
    /// </summary>
    public bool HasPartialKey { get; init; }

    /// <summary>
    /// Whether this file counts as key-tagged. Note this is stricter than "has an entry" —
    /// an entry exists for any file with <em>any</em> keyword, so a file tagged only
    /// <c>Type|Vocal</c> has an entry and no key.
    /// </summary>
    public bool IsKeyTagged => Key is not null;
}
