using System.Xml.Linq;

namespace Crateside.Core.Staging;

/// <summary>
/// Reads the <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c> keyword index for a root folder.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Read only, never write.</strong> Ableton owns this file while it is running
/// (keytag ADR 0001). The board is allowed to lag until Ableton flushes; it is not allowed
/// to touch the file.
/// </para>
/// <para>
/// Ableton migrates an entry when a file moves and drops entries for absent files, so the
/// index is self-cleaning — there is no reconciliation to do here.
/// </para>
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
    /// Reads the index for <paramref name="rootFolder"/>. Returns an empty index — never
    /// throws — when the folder has no XMP yet, or the file is mid-write, or it is malformed.
    /// A root Ableton has never indexed is an ordinary state, not an error.
    /// </summary>
    public AbletonFolderInfo Read(string rootFolder)
    {
        var xmpPath = FindXmp(rootFolder);

        if (xmpPath is null)
            return AbletonFolderInfo.Empty(rootFolder);

        try
        {
            // Ableton may hold the file open; share everything so a running Live does not
            // turn the board into an error page.
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            System.Xml.XmlException)
        {
            return AbletonFolderInfo.Empty(rootFolder);
        }
    }

    /// <summary>
    /// Locates the XMP by its containing folder. There is one per root, and its UUID name is
    /// per-install rather than per-folder, so the name is never matched on.
    /// </summary>
    private static string? FindXmp(string rootFolder)
    {
        var directory = Path.Combine(rootFolder, FolderInfoDirectory);

        if (!Directory.Exists(directory))
            return null;

        // Ordered so a second file — never yet observed — resolves the same way every run
        // instead of by directory-enumeration luck.
        return Directory.EnumerateFiles(directory, "*.xmp").Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static AbletonFileTags? ParseEntry(XElement item)
    {
        if (item.Element(AblFr + "filePath")?.Value is not { Length: > 0 } filePath)
            return null;

        var keywords = item
            .Element(AblFr + "keywords")
            ?.Descendants(Rdf + "li")
            .Select(li => li.Value)
            .Where(value => value.Length > 0)
            .ToArray() ?? [];

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
    /// Recombines the two independent key keywords into the ledger's spelling. Ableton writes
    /// the tonic and the mode as separate keywords using Unicode ♯ (<c>Key|G♯</c> +
    /// <c>Key|Major</c>); the ledger spells it ASCII and lowercase (<c>G# major</c>).
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
