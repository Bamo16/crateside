using AbleKit.Analysis;
using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Marks;
using Crateside.Core.Staging;
using Crateside.Core.Warps;
using Crateside.Tests.Ableton;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Staging;

public sealed class StagingMarksTests : IDisposable
{
    private const int Frames = 22_050;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly WrittenWarps _written = new();
    private readonly LiveAnalyses _waiting = new();
    private readonly FolderInfoReader _reader = new();
    private readonly TrackMarkStore _trackMarks;
    private readonly StagingMarks _marks;

    public StagingMarksTests()
    {
        _paths = new CratesidePaths { StagingFolder = Path.Combine(_root, "Staging") };
        Directory.CreateDirectory(_paths.StagingFolder);

        _trackMarks = new TrackMarkStore(TestLedger.Store());
        _marks = MarksWith(TestCollections.Store(_root));
    }

    private StagingMarks MarksWith(CollectionSettingsStore collections)
    {
        var origins = new TransientOrigins();

        return new StagingMarks(
            _paths,
            new StemWarpReader(_written, origins, _waiting),
            new SidecarWriter(new NoDecoder(), new OnsetDetector(), _written, origins),
            _waiting,
            TestLedger.Store(),
            _trackMarks,
            _reader,
            new FolderInfoWriter(),
            collections
        );
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Each_stem_is_put_in_the_Collection_of_its_warp_state()
    {
        var saved = Stem("Song (Instrumental - Full).flac", "Instrumental - Full");
        Sidecar(saved, markerBeat: 0);
        var onTheGrid = Stem("Song (Vocal - Full).flac", "Vocal - Full");
        Sidecar(onTheGrid, markerBeat: 1);
        _written.RecordStarter(onTheGrid);
        var unwarped = Stem("Song (Drums).flac", "Drums");
        var warpOff = Stem("Other (Instrumental - Full).flac", "Instrumental - Full");
        Sidecar(warpOff, markerBeat: 0);
        _written.RecordUnwarped(warpOff);

        Assert.Equal(new TagWriteOutcome.Written(4), _marks.Collect());

        var info = _reader.Read(_paths.StagingFolder);
        Assert.Equal(
            [TestCollections.Number(BrowserCollection.Warped)],
            CollectionsOf(info, saved)
        );
        Assert.Equal(
            [TestCollections.Number(BrowserCollection.CheckBeat)],
            CollectionsOf(info, onTheGrid)
        );
        Assert.Equal(
            [TestCollections.Number(BrowserCollection.WarpByHand)],
            CollectionsOf(info, unwarped)
        );
        Assert.Equal(
            [TestCollections.Number(BrowserCollection.WarpByHand)],
            CollectionsOf(info, warpOff)
        );
    }

    [Fact]
    public void A_Collection_the_user_put_a_file_in_survives_and_a_settled_state_is_not_rewritten()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        new FolderInfoWriter().Apply(
            _paths.StagingFolder,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|A", "Key|Minor"],
                    Collections = [1, 2],
                },
            ]
        );

        Assert.Equal(new TagWriteOutcome.Written(1), _marks.Collect());
        Assert.Equal(new TagWriteOutcome.Written(0), _marks.Collect());

        Assert.True(_reader.Read(_paths.StagingFolder).TryGet(stem, out var tags));
        Assert.Equal([1, TestCollections.Number(BrowserCollection.WarpByHand)], tags.Collections);
        Assert.Equal(["Key|A", "Key|Minor"], tags.Keywords);
    }

    [Fact]
    public void A_Collection_the_user_set_on_a_number_no_meaning_uses_stays()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        new FolderInfoWriter().Apply(
            _paths.StagingFolder,
            [new() { RelativePath = Path.GetFileName(stem), Collections = [3] }]
        );

        _marks.Collect();

        Assert.Equal(
            [3, TestCollections.Number(BrowserCollection.WarpByHand)],
            CollectionsOf(_reader.Read(_paths.StagingFolder), stem)
        );
    }

    [Fact]
    public void With_every_meaning_off_the_tag_store_is_left_as_it_was()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        _trackMarks.Set("abcdefghijk", TrackMark.OddBars, true);
        new FolderInfoWriter().Apply(
            _paths.StagingFolder,
            [new() { RelativePath = Path.GetFileName(stem), Collections = [2, 3] }]
        );
        var off = MarksWith(TestCollections.Store(Path.Combine(_root, "off"), new()));

        Assert.Equal(new TagWriteOutcome.Written(0), off.Collect());

        Assert.Equal([2, 3], CollectionsOf(_reader.Read(_paths.StagingFolder), stem));
    }

    [Fact]
    public void A_meaning_mapped_to_another_number_is_written_there_and_only_there()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        new FolderInfoWriter().Apply(
            _paths.StagingFolder,
            [new() { RelativePath = Path.GetFileName(stem), Collections = [1, 2, 7] }]
        );
        var moved = MarksWith(
            TestCollections.Store(Path.Combine(_root, "moved"), new() { WarpByHand = 3 })
        );

        moved.Collect();

        Assert.Equal([1, 2, 3, 7], CollectionsOf(_reader.Read(_paths.StagingFolder), stem));
    }

    [Fact]
    public void A_stem_of_a_track_marked_as_changing_key_is_put_in_Key_caveat_until_unmarked()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        _trackMarks.Set("abcdefghijk", TrackMark.ChangesKey, true);

        _marks.Collect();

        Assert.Equal(
            [
                TestCollections.Number(BrowserCollection.WarpByHand),
                TestCollections.Number(BrowserCollection.KeyCaveat),
            ],
            CollectionsOf(_reader.Read(_paths.StagingFolder), stem)
        );

        _trackMarks.Set("abcdefghijk", TrackMark.ChangesKey, false);
        _marks.Collect();

        Assert.Equal(
            [TestCollections.Number(BrowserCollection.WarpByHand)],
            CollectionsOf(_reader.Read(_paths.StagingFolder), stem)
        );
    }

    [Fact]
    public void A_stem_of_a_track_marked_odd_bars_is_put_in_Odd_bars()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        _trackMarks.Set("abcdefghijk", TrackMark.OddBars, true);

        _marks.Collect();

        Assert.Equal(
            [
                TestCollections.Number(BrowserCollection.WarpByHand),
                TestCollections.Number(BrowserCollection.OddBars),
            ],
            CollectionsOf(_reader.Read(_paths.StagingFolder), stem)
        );
    }

    [Fact]
    public void A_saved_warp_takes_its_stems_color_and_stays_the_users()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        Sidecar(stem, markerBeat: 0);

        Assert.Equal([stem], _marks.Recolor());
        Assert.Empty(_marks.Recolor());

        var sidecar = AnalysisFile.Read($"{stem}.asd");
        Assert.Equal(ClipColors.For(stem), sidecar.Clip.Color);
        Assert.False(_written.IsCratesides(stem));
    }

    [Fact]
    public void A_starter_recolored_is_still_crateside_s_to_replace()
    {
        var stem = Stem("Song (Vocal - Full).flac", "Vocal - Full");
        Sidecar(stem, markerBeat: 1);
        _written.RecordStarter(stem);

        Assert.Equal([stem], _marks.Recolor());

        Assert.True(_written.IsStarter(stem));
    }

    [Fact]
    public void A_sidecar_with_no_saved_clip_is_left_for_Live()
    {
        var stem = Stem("Song (Drums).flac", "Drums");
        Sidecar(stem, markerBeat: 0, saved: false);

        Assert.Empty(_marks.Recolor());
    }

    private static int[] CollectionsOf(FolderInfo info, string path) =>
        info.TryGet(path, out var tags) ? [.. tags.Collections] : [];

    private string Stem(string name, string preset)
    {
        var path = Path.Combine(_paths.StagingFolder, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.4.0.0 | preset: {preset} | source: https://www.youtube.com/watch?v=abcdefghijk";
        file.Save();

        return path;
    }

    private static void Sidecar(string path, double markerBeat, bool saved = true) =>
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                new float[Frames * 2],
                2,
                [],
                new FileInfo(path).Length
            ),
            Warp = new Warp { Markers = [new(0.1, markerBeat), new(0.4, markerBeat + 1)] },
            Clip = new Clip
            {
                LoopStart = markerBeat,
                LoopEnd = markerBeat + 1,
                LoopOn = false,
            },
            IsDefaultClipSaved = saved,
        }.Write($"{path}.asd");

    private sealed class NoDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(null);
    }
}
