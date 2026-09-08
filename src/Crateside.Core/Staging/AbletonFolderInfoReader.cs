using System.Xml.Linq;

namespace Crateside.Core.Staging;

/// <summary>
/// Reads the <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c> keyword index for a root folder.
/// </summary>
/// <remarks>
/// Read only, never write — Ableton owns this file while it is running.
/// </remarks>
public sealed class AbletonFolderInfoReader
{
    private const string FolderInfoDirectory = "Ableton Folder Info";

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace AblFr = "https://ns.ableton.com/xmp/fs-resources/1.0/";

    private const string KeywordSeparator = "|";
    private const string KeyCategory = "Key";
    private const string TypeCategory = "Type";

    /// <summary>
    /// Reads the index for <paramref name="rootFolder"/>. Never throws — returns an empty
    /// index when the folder has no XMP yet, the file is mid-write, or it is malformed.
    /// </summary>
    public AbletonFolderInfo Read(string rootFolder)
    {
        var xmpPath = FindXmp(rootFolder);

        if (xmpPath is null)
            return AbletonFolderInfo.Empty(rootFolder);

        try
        {
            using var stream = new FileStream(
                xmpPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            var document = XDocument.Load(stream);

            var entries = document
                .Descendants(AblFr + "items")
                .Descendants(Rdf + "li")
                .Select(ParseEntry)
                .OfType<AbletonFileTags>()
                .ToArray();

            return new AbletonFolderInfo(rootFolder, entries);
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return AbletonFolderInfo.Empty(rootFolder);
        }
    }

    private static string? FindXmp(string rootFolder)
    {
        var directory = Path.Combine(rootFolder, FolderInfoDirectory);

        if (!Directory.Exists(directory))
            return null;

        return Directory
            .EnumerateFiles(directory, "*.xmp")
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static AbletonFileTags? ParseEntry(XElement item)
    {
        if (item.Element(AblFr + "filePath")?.Value is not { Length: > 0 } filePath)
            return null;

        var keywords =
            item.Element(AblFr + "keywords")
                ?.Descendants(Rdf + "li")
                .Select(li => li.Value)
                .Where(value => value.Length > 0)
                .ToArray()
            ?? [];

        var (key, isPartial) = ReadKey(keywords);

        return new AbletonFileTags
        {
            RelativePath = filePath,
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
