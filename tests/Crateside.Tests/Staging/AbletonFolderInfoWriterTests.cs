using System.Text;
using System.Xml.Linq;
using Crateside.Core.Ableton;

namespace Crateside.Tests.Staging;

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
        // ADR 0003 finding 6.
        var outcome = _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        Assert.True(outcome.IsSuccess);
        Assert.True(_reader.Read(_root).TryGet("a.flac", out var tags));
        Assert.Equal("A minor", tags.Key);
    }

    [Fact]
    public void The_file_has_no_declaration_no_bom_and_lf_endings()
    {
        // Measured from Ableton's own file.
        _writer.Apply(_root, [Assign("a.flac", "Key|A", "Key|Minor")]);

        var bytes = File.ReadAllBytes(XmpPath);

        Assert.False(bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF, "wrote a BOM");
        Assert.StartsWith("<x:xmpmeta", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void Entries_crateside_did_not_create_survive_with_their_order()
    {
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

        Assert.True(info.TryGet("second.flac", out var second));
        Assert.Equal("F major", second.Key);
        Assert.Equal("Vocal", second.Type);
    }

    [Fact]
    public void A_keyword_Ableton_does_not_already_know_is_refused()
    {
        // ADR 0003 finding 8: Ableton would register a second, permanent "F#".
        var outcome = _writer.Apply(_root, [Assign("a.flac", "Key|F#", "Key|Major")]);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsStale, "a bad keyword is not fixed by retrying");
        Assert.Contains("Key|F#", outcome.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "Ableton Folder Info")));
    }

    [Fact]
    public void One_bad_keyword_stops_the_whole_write()
    {
        var outcome = _writer.Apply(
            _root,
            [Assign("good.flac", "Key|A", "Key|Minor"), Assign("bad.flac", "Key|H", "Key|Major")]
        );

        Assert.False(outcome.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(_root, "Ableton Folder Info")));
    }

    /// <summary>Driven directly: the moment between read and replace is unreachable from Apply.</summary>
    [Fact]
    public void A_store_that_changed_under_the_write_is_left_alone()
    {
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

        Assert.Empty(
            Directory.EnumerateFiles(Path.Combine(_root, "Ableton Folder Info"), "*.crateside-tmp")
        );
    }

    [Fact]
    public void A_store_nobody_touched_is_replaced()
    {
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
        // Taken from the 451 key keywords across the real roots: sharps as U+266F, no flats.
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
