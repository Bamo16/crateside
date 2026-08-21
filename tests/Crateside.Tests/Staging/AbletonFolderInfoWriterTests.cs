using System.Text;
using System.Xml.Linq;
using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// The one writer crateside has that shares a file with another running program.
/// </summary>
public class AbletonFolderInfoWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly AbletonFolderInfoWriter _writer = new();
    private readonly AbletonFolderInfoReader _reader = new();

    public AbletonFolderInfoWriterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string XmpPath =>
        Directory.EnumerateFiles(Path.Combine(_root, "Ableton Folder Info"), "*.xmp").Single();

    private static TagAssignment Assign(string name, params string[] keywords) =>
        new() { RelativePath = name, Keywords = keywords };

    [Fact]
    public void A_store_is_created_where_there_is_none()
    {
        // ADR 0003 finding 6: crateside can create the file, directory included, for a file
        // Ableton has never indexed, and Ableton honours it.
        var outcome = _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        Assert.True(outcome.IsSuccess);
        Assert.True(_reader.Read(_root).TryGet("a.flac", out var tags));
        Assert.Equal("A minor", tags.Key);
    }

    [Fact]
    public void The_file_has_no_declaration_no_bom_and_lf_endings()
    {
        // Measured from Ableton's own file rather than assumed. A writer that adds any of these
        // is not round-tripping, whatever the XML says.
        _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        var bytes = File.ReadAllBytes(XmpPath);

        Assert.False(bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF, "wrote a BOM");
        Assert.StartsWith("<x:xmpmeta", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void Entries_crateside_did_not_create_survive_with_their_order()
    {
        // Ableton merges rather than rewriting from memory, and crateside has to hold up its
        // side of that -- these entries are the user's work.
        _writer.Apply(
            _root,
            [
                Assign("first.flac", "Key|C", "Key|Major"),
                Assign("second.flac", "Key|D", "Key|Minor"),
                Assign("third.flac", "Key|E", "Key|Major"),
            ]
        );

        _writer.Apply(_root, [Assign("second.flac", "Key|F", "Key|Major", "Type|Vocal")]);

        var info = _reader.Read(_root);

        Assert.Equal(
            ["first.flac", "second.flac", "third.flac"],
            info.Entries.Select(e => e.RelativePath)
        );

        // Rewritten in place, so it keeps its position rather than moving to the end.
        Assert.True(info.TryGet("second.flac", out var second));
        Assert.Equal("F major", second.Key);
        Assert.Equal("Vocal", second.Type);
    }

    [Fact]
    public void A_keyword_Ableton_does_not_already_know_is_refused()
    {
        // ADR 0003 finding 8: an ASCII hash is not rejected by Ableton -- it silently registers
        // a second, permanent "F#" beside the built-in one, removable only by hand. So the
        // guard has to be here, before the bytes are written.
        var outcome = _writer.Apply(_root, [Assign("a.flac", "Key|F#", "Key|Major")]);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsStale, "a bad keyword is not fixed by retrying");
        Assert.Contains("Key|F#", outcome.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "Ableton Folder Info")));
    }

    [Fact]
    public void One_bad_keyword_stops_the_whole_write()
    {
        // A partial write would leave the invalid one to be retried on its own -- and the
        // invalid one is precisely the one that does permanent damage.
        var outcome = _writer.Apply(
            _root,
            [Assign("good.flac", "Key|A", "Key|Minor"), Assign("bad.flac", "Key|H", "Key|Major")]
        );

        Assert.False(outcome.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(_root, "Ableton Folder Info")));
    }

    /// <summary>
    /// The compare-and-swap itself, driven directly.
    /// </summary>
    /// <remarks>
    /// The interesting moment is <em>between</em> the read and the replace, and no test can
    /// reach it from outside <c>Apply</c> — changing the file first only means <c>Apply</c>
    /// reads the newer stamp and is perfectly happy. So the swap is exercised as the unit it
    /// is, with a stamp that no longer matches the file.
    /// </remarks>
    [Fact]
    public void A_store_that_changed_under_the_write_is_left_alone()
    {
        // Ableton writes this file in place (finding 11), so a write built on a stale read
        // would drop whatever it did in between.
        _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        var path = XmpPath;
        var before = File.ReadAllBytes(path);
        var stale = File.GetLastWriteTimeUtc(path).AddSeconds(-30);

        var replaced = AbletonFolderInfoWriter.Replace(
            path,
            XDocument.Parse("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" />"),
            stale
        );

        Assert.False(replaced);
        Assert.Equal(before, File.ReadAllBytes(path));

        // And the half-written candidate does not outlive the attempt.
        Assert.Empty(
            Directory.EnumerateFiles(Path.Combine(_root, "Ableton Folder Info"), "*.crateside-tmp")
        );
    }

    [Fact]
    public void A_store_nobody_touched_is_replaced()
    {
        // The other half of the same decision: an unchanged stamp must not block the write, or
        // nothing would ever be tagged.
        _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        var path = XmpPath;

        Assert.True(
            AbletonFolderInfoWriter.Replace(
                path,
                XDocument.Parse("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" />"),
                File.GetLastWriteTimeUtc(path)
            )
        );
    }

    [Fact]
    public void Every_tonic_Ableton_knows_is_accepted_and_nothing_else_is()
    {
        // The closed set, taken from the 451 key keywords across the user's real roots: twelve
        // tonics, sharps as U+266F, no flats.
        Assert.Equal(12, AbletonFolderInfoWriter.Tonics.Count);

        Assert.All(
            AbletonFolderInfoWriter.Tonics,
            t => Assert.True(AbletonFolderInfoWriter.IsValidKeyword($"Key|{t}"))
        );

        Assert.False(AbletonFolderInfoWriter.IsValidKeyword("Key|Bb"));
        Assert.False(AbletonFolderInfoWriter.IsValidKeyword("Key|A♭"));
        Assert.False(AbletonFolderInfoWriter.IsValidKeyword("Key|C#"));
        Assert.False(AbletonFolderInfoWriter.IsValidKeyword("Mood|Dark"));
    }
}
