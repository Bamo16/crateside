using System.Xml.Linq;

namespace Crateside.Core.Staging;

/// <summary>
/// Reads the <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c> keyword index for a root folder.
/// </summary>
public sealed class AbletonFolderInfoReader
{
    private const string FolderInfoDirectory = "Ableton Folder Info";

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace AblFr = "https://ns.ableton.com/xmp/fs-resources/1.0/";

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

    private static string? FindXmp(string rootFolder) => AbletonFolderInfoFile.In(rootFolder);

    private static AbletonFileTags? ParseEntry(XElement item)
    {
        if (item.Element(AblFr + "filePath")?.Value is not { Length: > 0 } filePath)
            return null;

        return AbletonFileTags.FromKeywords(
            filePath,
            Values(item, "keywords"),
            Values(item, "hideKeywords")
        );
    }

    private static string[] Values(XElement item, string element) =>
        item.Element(AblFr + element)
            ?.Descendants(Rdf + "li")
            .Select(li => li.Value)
            .Where(value => value.Length > 0)
            .ToArray()
        ?? [];
}
