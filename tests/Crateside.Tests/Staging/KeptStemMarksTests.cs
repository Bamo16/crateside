using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Staging;
using Crateside.Tests.Ableton;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Staging;

public sealed class KeptStemMarksTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly FolderInfoReader _reader = new();
    private readonly TrackMarkStore _trackMarks;
    private readonly KeptStemMarks _marks;

    public KeptStemMarksTests()
    {
        _paths = new CratesidePaths { KeptWarpGuides = Path.Combine(_root, "Drums") };
        Directory.CreateDirectory(_paths.KeptWarpGuides);

        _trackMarks = new TrackMarkStore(TestLedger.Store());
        _marks = new KeptStemMarks(
            _paths,
            TestLedger.Store(),
            _trackMarks,
            _reader,
            new FileIndexReader(Path.Combine(_root, "no-index")),
            new FolderInfoWriter(),
            new TagStoreBackups(Path.Combine(_root, "backups"), new FakeTimeProvider()),
            TestCollections.Store(_root)
        );
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_kept_stem_takes_its_tracks_marks_and_loses_them_when_they_are_cleared()
    {
        var stem = Stem("Song (Drums).flac");
        _trackMarks.Set("abcdefghijk", TrackMark.OddBars, true);

        Assert.Equal(new TagWriteOutcome.Written(1), _marks.Collect());
        Assert.Equal([TestCollections.Number(BrowserCollection.OddBars)], CollectionsOf(stem));

        _trackMarks.Set("abcdefghijk", TrackMark.OddBars, false);
        _marks.Collect();

        Assert.Empty(CollectionsOf(stem));
    }

    [Fact]
    public void A_Collection_the_user_chose_survives_and_a_settled_stem_is_not_rewritten()
    {
        var stem = Stem("Song (Drums).flac");
        new FolderInfoWriter().Apply(
            _paths.KeptWarpGuides,
            [new() { RelativePath = Path.GetFileName(stem), Collections = [1] }]
        );
        _trackMarks.Set("abcdefghijk", TrackMark.ChangesKey, true);

        Assert.Equal(new TagWriteOutcome.Written(1), _marks.Collect());
        Assert.Equal(new TagWriteOutcome.Written(0), _marks.Collect());
        Assert.Equal([1, TestCollections.Number(BrowserCollection.KeyCaveat)], CollectionsOf(stem));
    }

    [Fact]
    public void A_folder_that_is_not_there_is_left_alone()
    {
        Directory.Delete(_paths.KeptWarpGuides);

        Assert.Equal(new TagWriteOutcome.Written(0), _marks.Collect());
    }

    private int[] CollectionsOf(string path) =>
        _reader.Read(_paths.KeptWarpGuides).TryGet(path, out var tags) ? [.. tags.Collections] : [];

    private string Stem(string name)
    {
        var path = Path.Combine(_paths.KeptWarpGuides, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            "stemforge/0.4.0.0 | preset: Drums | source: https://www.youtube.com/watch?v=abcdefghijk";
        file.Save();

        return path;
    }
}
