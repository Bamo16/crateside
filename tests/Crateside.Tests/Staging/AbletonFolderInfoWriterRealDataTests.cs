using System.Xml.Linq;
using Crateside.Core;
using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>The writer against the tag store Ableton actually wrote.</summary>
/// <remarks>Never writes to the real roots: everything happens on a copy.</remarks>
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

    /// <remarks>
    /// Not byte-for-byte: Ableton wraps only <c>rdf:Description</c>'s namespace declarations, which
    /// <c>XmlWriter</c> cannot match. ADR 0003 findings 4 and 5 are why that is enough.
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
        // The validator is a closed set derived from this library, so it must accept all of it.
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
