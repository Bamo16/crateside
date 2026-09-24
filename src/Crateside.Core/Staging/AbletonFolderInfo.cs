using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Genres;

namespace Crateside.Core.Staging;

/// <summary>
/// The keyword index Ableton keeps for one root folder, from its XMP or Ableton's own database.
/// Key it on the root folder, never on the XMP's per-install UUID.
/// </summary>
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
    /// Keywords the user took off this file, never to be written back. Only the XMP carries them;
    /// read from the index, always empty.
    /// </summary>
    public IReadOnlyList<string> HiddenKeywords { get; init; } = [];

    /// <summary>
    /// The tonic and mode keywords recombined in the ledger's spelling (<c>G# major</c>), or null
    /// for no key or only half of one.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>The <c>Type|</c> keyword — Instrumental, Vocal, Loop.</summary>
    public string? Type { get; init; }

    /// <summary>The <c>Genre|</c> keyword crateside writes, or null (ADR 0030).</summary>
    public string? Genre { get; init; }

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
        IReadOnlyList<string> keywords,
        IReadOnlyList<string>? hidden = null
    )
    {
        var (key, isPartial) = ReadKey(keywords);

        return new AbletonFileTags
        {
            RelativePath = relativePath,
            Keywords = keywords,
            HiddenKeywords = hidden ?? [],
            Key = key,
            Type = Values(keywords, TypeCategory).FirstOrDefault(),
            Genre = Values(keywords, GenreVocabulary.Group).FirstOrDefault(),
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
