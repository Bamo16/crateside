using System.Xml.Linq;
using Crateside.Core;
using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// The writer against the tag store Ableton actually wrote.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0003 requires crateside to match Ableton's format byte-for-byte, and every fixture in
/// the unit tests is a file crateside wrote itself — which proves only that it agrees with
/// itself. This reads the user's real store and checks that writing it back changes nothing.
/// </para>
/// <para>
/// <strong>Never writes to the real roots.</strong> Everything happens on a copy in the
/// temporary directory. Skips when the machine has no such store.
/// </para>
/// </remarks>
public class AbletonFolderInfoWriterRealDataTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public AbletonFolderInfoWriterRealDataTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Copies a real store into the test's own root, so nothing real is at risk.</summary>
    private string? CopyRealStore()
    {
        var source = new[]
        {
            new CratesidePaths().SampleStaging,
            new CratesidePaths().MashupSamples,
        }
            .Select(r => Path.Combine(r, "Ableton Folder Info"))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.xmp"))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        if (source is null)
            return null;

        var directory = Path.Combine(_root, "Ableton Folder Info");
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, Path.GetFileName(source));
        File.Copy(source, destination);

        return destination;
    }

    /// <summary>
    /// Writing Ableton's own file back preserves every entry, and the format rules that matter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Not byte-for-byte, and it cannot be.</strong> Ableton's XMP toolkit wraps the
    /// four namespace declarations on <c>rdf:Description</c> one per line while writing every
    /// other element's attributes inline. <c>XmlWriter</c> offers all-or-nothing
    /// (<c>NewLineOnAttributes</c>), so matching that would mean hand-rolling an XML writer —
    /// and an escaping bug in one of those would do far more damage than the cosmetic
    /// difference it fixed.
    /// </para>
    /// <para>
    /// What is checked instead is everything that can affect Ableton: the entries, their order,
    /// their keywords, and the four byte-level properties measured off the real file. ADR 0003
    /// finding 5 is what makes that sufficient — disk is authoritative and Ableton honoured a
    /// hand-written file — and finding 4 means it reformats to its own style on its next merge
    /// anyway.
    /// </para>
    /// </remarks>
    [Fact]
    public void Writing_Abletons_own_file_back_preserves_it()
    {
        var path = CopyRealStore();
        Assert.SkipWhen(path is null, "no Ableton tag store on this machine");

        var reader = new AbletonFolderInfoReader();
        var before = reader.Read(_root).Entries;

        AbletonFolderInfoWriter.Replace(
            path!,
            XDocument.Parse(File.ReadAllText(path!)),
            File.GetLastWriteTimeUtc(path!)
        );

        var after = reader.Read(_root).Entries;

        Assert.Equal(
            before.Select(e => (e.RelativePath, string.Join(",", e.Keywords))),
            after.Select(e => (e.RelativePath, string.Join(",", e.Keywords)))
        );

        var bytes = File.ReadAllBytes(path!);

        Assert.False(bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF, "wrote a BOM");
        Assert.StartsWith("<x:xmpmeta", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    [Fact]
    public void Every_keyword_already_in_the_library_is_one_the_writer_would_accept()
    {
        // The validator is a closed set derived from this library. If it rejects something the
        // library already contains, the set is wrong -- and a keyword crateside refuses to
        // write is a stem it silently never tags.
        var reader = new AbletonFolderInfoReader();
        var paths = new CratesidePaths();

        var keywords = new[] { paths.SampleStaging, paths.MashupSamples }
            .Where(Directory.Exists)
            .SelectMany(r => reader.Read(r).Entries)
            .SelectMany(e => e.Keywords)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.SkipWhen(keywords.Count is 0, "no tagged stems on this machine");

        var refused = keywords.Where(k => !AbletonFolderInfoWriter.IsValidKeyword(k)).ToList();

        Assert.True(refused.Count is 0, $"the writer would refuse: {string.Join(", ", refused)}");
    }
}
