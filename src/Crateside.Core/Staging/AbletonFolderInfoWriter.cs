using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Crateside.Core.Staging;

/// <summary>
/// Writes keywords into a root's <c>Ableton Folder Info\&lt;uuid&gt;.xmp</c>, alongside whatever
/// Ableton has put there.
/// </summary>
/// <remarks>
/// <para>
/// Sanctioned by ADR 0003, which measured that Ableton does not own this file exclusively: it
/// is unlocked, it merges from disk rather than rewriting from an in-memory index, and it picks
/// up an external edit live — including into the search index, which is what the tags are for.
/// </para>
/// <para>
/// <strong>Ableton writes this file in place</strong> (ADR 0003 finding 11 — same file id, no
/// rename, and it shrank a file by 453 bytes that way). So there is a window in which the bytes
/// on disk are neither the old content nor the new. That is the whole reason for the two
/// defences here: crateside replaces atomically so it can never leave a torn file of its own,
/// and it re-checks the file's timestamp immediately before replacing so a write built on a
/// stale read turns into a retry rather than dropping whatever Ableton did in between.
/// </para>
/// </remarks>
public sealed class AbletonFolderInfoWriter
{
    private const string FolderInfoDirectory = "Ableton Folder Info";

    private static readonly XNamespace X = "adobe:ns:meta/";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace AblFr = "https://ns.ableton.com/xmp/fs-resources/1.0/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";

    /// <summary>
    /// Ableton's built-in tonics. Sharps, spelled with <strong>U+266F</strong>, and no flats.
    /// </summary>
    /// <remarks>
    /// Taken from the 451 key keywords across the user's two real roots rather than from theory:
    /// exactly these twelve occur, and every sharp is U+266F. ADR 0003 finding 8 is why the set
    /// is closed rather than advisory — a <c>Key|F#</c> with an ASCII hash is not rejected by
    /// Ableton, it silently registers as a <em>new user-created tag</em> that then sits beside
    /// the built-in one forever and can only be removed by hand.
    /// </remarks>
    public static readonly IReadOnlySet<string> Tonics = new HashSet<string>(StringComparer.Ordinal)
    {
        "C",
        "C♯",
        "D",
        "D♯",
        "E",
        "F",
        "F♯",
        "G",
        "G♯",
        "A",
        "A♯",
        "B",
    };

    private static readonly IReadOnlySet<string> Modes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Major",
        "Minor",
    };

    private static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "Vocal",
        "Instrumental",
        "Loop",
    };

    /// <summary>
    /// The exact bytes Ableton writes: no XML declaration, no BOM, LF line endings even on
    /// Windows, three-space indents. Measured from the real file, not assumed.
    /// </summary>
    private static readonly XmlWriterSettings Format = new()
    {
        OmitXmlDeclaration = true,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        NewLineChars = "\n",
        Indent = true,
        IndentChars = "   ",
    };

    /// <summary>
    /// Applies keywords to files under <paramref name="rootFolder"/>, leaving every other entry
    /// and its position alone.
    /// </summary>
    /// <remarks>
    /// Nothing is written unless every keyword passes <see cref="IsValidKeyword"/>. A partial
    /// write of a valid subset would be the worse outcome: the invalid one is the one that
    /// pollutes the vocabulary permanently, and finding out later which files were done is
    /// harder than doing them all again.
    /// </remarks>
    public TagWriteOutcome Apply(string rootFolder, IReadOnlyList<TagAssignment> assignments)
    {
        if (assignments.Count is 0)
            return TagWriteOutcome.Written(0);

        if (
            assignments.SelectMany(a => a.Keywords).FirstOrDefault(k => !IsValidKeyword(k)) is
            { } bad
        )
            return TagWriteOutcome.Rejected($"'{bad}' is not a keyword Ableton already knows");

        var directory = Path.Combine(rootFolder, FolderInfoDirectory);
        var path = ExistingXmp(directory);

        try
        {
            Directory.CreateDirectory(directory);

            // Captured before the read, so anything Ableton does from here on is caught by the
            // check below rather than silently overwritten.
            var stamp = path is not null ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
            var document = path is not null ? Load(path) : NewDocument();

            if (document is null)
                return TagWriteOutcome.Stale("the tag store was mid-write and could not be read");

            var items = Items(document);

            foreach (var assignment in assignments)
                ApplyOne(items, assignment);

            Stamp(document);

            return Replace(path ?? Path.Combine(directory, NewName()), document, stamp)
                ? TagWriteOutcome.Written(assignments.Count)
                : TagWriteOutcome.Stale("the tag store changed while it was being updated");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return TagWriteOutcome.Stale(e.Message);
        }
    }

    /// <summary>
    /// Points existing entries at new file names, keeping their keywords and their position.
    /// </summary>
    /// <remarks>
    /// A file with no entry is skipped rather than invented: an entry records tags Ableton holds,
    /// and a file that has none is untagged, which the tagging stage fixes on its own pass.
    /// </remarks>
    public TagWriteOutcome Rename(string rootFolder, IReadOnlyList<TagRename> renames)
    {
        if (renames.Count is 0)
            return TagWriteOutcome.Written(0);

        var path = ExistingXmp(Path.Combine(rootFolder, FolderInfoDirectory));

        // No store at all is not a failure. It means Ableton has never indexed this root and
        // there is nothing carrying the old names.
        if (path is null)
            return TagWriteOutcome.Written(0);

        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            var document = Load(path);

            if (document is null)
                return TagWriteOutcome.Stale("the tag store was mid-write and could not be read");

            var items = Items(document);

            var moved = 0;

            foreach (var rename in renames)
            {
                if (Find(items, rename.From)?.Element(AblFr + "filePath") is not { } filePath)
                    continue;

                filePath.Value = rename.To;
                moved++;
            }

            if (moved is 0)
                return TagWriteOutcome.Written(0);

            Stamp(document);

            return Replace(path, document, stamp)
                ? TagWriteOutcome.Written(moved)
                : TagWriteOutcome.Stale("the tag store changed while it was being updated");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return TagWriteOutcome.Stale(e.Message);
        }
    }

    /// <summary>
    /// Whether a keyword is one Ableton already has, rather than one this write would invent.
    /// </summary>
    public static bool IsValidKeyword(string keyword) =>
        keyword.Split('|') switch
        {
            ["Key", var value] => Tonics.Contains(value) || Modes.Contains(value),
            ["Type", var value] => Types.Contains(value),
            _ => false,
        };

    /// <summary>
    /// Reads the file, or reports nothing if it cannot be parsed.
    /// </summary>
    /// <remarks>
    /// <strong>A parse failure means "Ableton is mid-write", not "the store is corrupt".</strong>
    /// Finding 11 makes that a real state rather than a defensive one, and the difference
    /// matters: the right response is to come back next tick, never to rebuild a file whose
    /// entries crateside mostly did not create.
    /// </remarks>
    private static XDocument? Load(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            return XDocument.Load(stream);
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes to a temporary file beside the target and swaps it in, but only if nothing has
    /// touched the target since it was read.
    /// </summary>
    /// <remarks>
    /// The temporary file is in the same directory deliberately — a replace across volumes is
    /// not atomic. There is still a sliver between the timestamp check and the swap, which is
    /// microseconds against the second-long window Ableton's own in-place writes occupy; it is
    /// narrowed rather than closed, and saying so is more useful than implying a lock exists.
    /// </remarks>
    internal static bool Replace(string path, XDocument document, DateTime? stamp)
    {
        var temporary = path + ".crateside-tmp";

        using (var buffer = new MemoryStream())
        {
            using (var writer = XmlWriter.Create(buffer, Format))
                document.Save(writer);

            // Ableton's file ends with a newline and XmlWriter's does not. Cosmetic, but this
            // is a file two programs take turns writing, and matching what can be matched keeps
            // a diff between them meaningful.
            buffer.Write("\n"u8);

            File.WriteAllBytes(temporary, buffer.ToArray());
        }

        if (stamp is { } read && File.GetLastWriteTimeUtc(path) != read)
        {
            File.Delete(temporary);

            return false;
        }

        if (File.Exists(path))
            File.Replace(temporary, path, destinationBackupFileName: null);
        else
            File.Move(temporary, path);

        return true;
    }

    private static void ApplyOne(XElement items, TagAssignment assignment)
    {
        var bag = new XElement(
            Rdf + "Bag",
            assignment.Keywords.Select(k => new XElement(Rdf + "li", k))
        );

        if (Find(items, assignment.RelativePath) is { } existing)
        {
            // Replaced in place, so the entry keeps its position. Ableton preserves insertion
            // order through its own merges (finding 4) and there is no reason to disturb it.
            existing.Element(AblFr + "keywords")?.Remove();
            existing.Add(new XElement(AblFr + "keywords", bag));

            return;
        }

        items.Add(
            new XElement(
                Rdf + "li",
                new XAttribute(Rdf + "parseType", "Resource"),
                new XElement(AblFr + "filePath", assignment.RelativePath),
                new XElement(AblFr + "keywords", bag)
            )
        );
    }

    private static XElement? Find(XElement items, string relativePath) =>
        items
            .Elements(Rdf + "li")
            .FirstOrDefault(li =>
                string.Equals(
                    Normalize(li.Element(AblFr + "filePath")?.Value),
                    Normalize(relativePath),
                    StringComparison.OrdinalIgnoreCase
                )
            );

    private static string Normalize(string? relativePath) =>
        (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');

    private static XElement Items(XDocument document) =>
        document.Descendants(AblFr + "items").Descendants(Rdf + "Bag").First();

    /// <summary>Ableton stamps this on every write; a file it wrote never lacks one.</summary>
    private static void Stamp(XDocument document)
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var description = document.Descendants(Rdf + "Description").First();

        if (description.Element(Xmp + "MetadataDate") is { } existing)
            existing.Value = now;
        else
            description.Add(new XElement(Xmp + "MetadataDate", now));
    }

    private static string? ExistingXmp(string directory) =>
        Directory.Exists(directory)
            ? Directory
                .EnumerateFiles(directory, "*.xmp")
                .Order(StringComparer.Ordinal)
                .FirstOrDefault()
            : null;

    /// <summary>
    /// The name for a store that does not exist yet. The UUID is per-install rather than
    /// per-folder — byte-identical across both of the user's roots — so a fresh one is a new
    /// identity and Ableton takes it (ADR 0003 finding 6).
    /// </summary>
    private static string NewName() => $"{Guid.NewGuid()}.xmp";

    private static XDocument NewDocument() =>
        new(
            new XElement(
                X + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName),
                new XAttribute(X + "xmptk", "XMP Core 6.0.0"),
                new XElement(
                    Rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName),
                    new XElement(
                        Rdf + "Description",
                        new XAttribute(Rdf + "about", string.Empty),
                        new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "ablFR", AblFr.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "xmp", Xmp.NamespaceName),
                        new XElement(Dc + "format", "application/vnd.ableton.folder"),
                        new XElement(AblFr + "resource", "folder"),
                        new XElement(AblFr + "platform", "win"),
                        new XElement(AblFr + "items", new XElement(Rdf + "Bag"))
                    )
                )
            )
        );
}

/// <summary>Keywords to put on one file, named the way Ableton names it.</summary>
public sealed record TagAssignment
{
    /// <summary>Path relative to the root folder.</summary>
    public required string RelativePath { get; init; }

    public required IReadOnlyList<string> Keywords { get; init; }
}

/// <summary>One entry's move to a new name, both relative to the root folder.</summary>
public sealed record TagRename
{
    public required string From { get; init; }
    public required string To { get; init; }
}

/// <summary>How one write turned out.</summary>
public sealed record TagWriteOutcome
{
    public required bool IsSuccess { get; init; }

    /// <summary>How many files were given keywords.</summary>
    public int Applied { get; init; }

    /// <summary>
    /// True when nothing was wrong with the request — the store simply moved underneath it.
    /// Worth telling apart from a rejection, because this one is fixed by trying again and the
    /// other never is.
    /// </summary>
    public bool IsStale { get; init; }

    public string? Error { get; init; }

    public static TagWriteOutcome Written(int applied) =>
        new() { IsSuccess = true, Applied = applied };

    public static TagWriteOutcome Stale(string error) =>
        new()
        {
            IsSuccess = false,
            IsStale = true,
            Error = error,
        };

    public static TagWriteOutcome Rejected(string error) =>
        new() { IsSuccess = false, Error = error };
}
