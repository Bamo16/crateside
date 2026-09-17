using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Staging;

/// <summary>
/// The keyword index Ableton keeps for one root folder, however it was read — from the folder's
/// <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c> or from Ableton's own database.
/// </summary>
/// <remarks>
/// The UUID in the XMP filename is per-install, not a folder identifier — key on the root
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

    /// <summary>What an unreadable or absent index yields.</summary>
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
    private const string KeywordSeparator = "|";
    private const string KeyCategory = "Key";
    private const string TypeCategory = "Type";

    /// <summary>Path relative to the root folder, as Ableton spells it.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Every keyword on the file, raw, including ones crateside does not read.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>
    /// Recombined from the separate tonic and mode keywords, normalized to the ledger's ASCII
    /// spelling (<c>G# major</c>).
    /// </summary>
    /// <remarks>
    /// Null when the file has no key, or only half of one — see <see cref="HasPartialKey"/>.
    /// </remarks>
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

    /// <summary>
    /// Interprets one file's raw keywords. Both stores spell keywords identically, so both
    /// readers come through here.
    /// </summary>
    internal static AbletonFileTags FromKeywords(
        string relativePath,
        IReadOnlyList<string> keywords
    )
    {
        var (key, isPartial) = ReadKey(keywords);

        return new AbletonFileTags
        {
            RelativePath = relativePath,
            Keywords = keywords,
            Key = key,
            Type = Values(keywords, TypeCategory).FirstOrDefault(),
            HasPartialKey = isPartial,
        };
    }

    /// <summary>
    /// Ableton writes the tonic using Unicode ♯/♭ (<c>Key|G♯</c>); recombined here into the
    /// ledger's ASCII, lowercase spelling (<c>G# major</c>).
    /// </summary>
    private static (string? Key, bool IsPartial) ReadKey(IReadOnlyList<string> keywords)
    {
        string? tonic = null;
        string? mode = null;

        foreach (var value in Values(keywords, KeyCategory))
        {
            if (IsMode(value))
                mode ??= value.ToLowerInvariant();
            else
                tonic ??= value.Replace('♯', '#').Replace('♭', 'b');
        }

        return (tonic, mode) switch
        {
            ({ } t, { } m) => ($"{t} {m}", false),
            (null, null) => (null, false),
            _ => (null, true), // half a key is not a key
        };
    }

    private static bool IsMode(string value) =>
        value.Equals("Major", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Minor", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Values(IReadOnlyList<string> keywords, string category)
    {
        var prefix = category + KeywordSeparator;

        return keywords
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..])
            .Where(v => v.Length > 0);
    }
}
