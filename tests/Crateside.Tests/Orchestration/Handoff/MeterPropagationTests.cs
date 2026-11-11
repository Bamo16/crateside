using System.Text.Json;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crateside.Tests.Orchestration.Handoff;

public sealed class MeterPropagationTests : IDisposable
{
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";
    private const string Song = "A - T";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly BackingRunner _runner = new(
        new FolderInfoReader(),
        new FolderInfoWriter(),
        NullLogger<BackingRunner>.Instance
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_genre_write_keeps_the_meter_and_every_keyword_it_does_not_own()
    {
        var vocal = Stem("Vocal - Full", "Key|A", "Key|Minor", "Meter|3/4", "Meter|Odd bars");

        _runner.Run(_root, Ledger(), ownedOf: _ => ["Genres|Hip Hop"], backKeys: false);

        Assert.Equal(
            ["Key|A", "Key|Minor", "Meter|3/4", "Meter|Odd bars", "Genres|Hip Hop"],
            KeywordsOf(vocal)
        );
    }

    [Fact]
    public void A_meter_set_on_one_stem_is_written_to_a_stem_without_one()
    {
        var vocal = Stem("Vocal - Full", "Key|A", "Key|Minor", "Meter|6/8", "Meter|Odd bars");
        var drums = Stem("Drums", "Key|A", "Key|Minor");
        var store = new MeterStore(Path.Combine(_root, "meters.json"));

        Assert.True(store.Settle(Song, _runner.MeterReadings(_root, Ledger(), index: null)));
        Pass(store);

        Assert.Equal(["Key|A", "Key|Minor", "Meter|6/8", "Meter|Odd bars"], KeywordsOf(drums));
        Assert.Equal(["Key|A", "Key|Minor", "Meter|6/8", "Meter|Odd bars"], KeywordsOf(vocal));
    }

    [Fact]
    public void Stems_that_disagree_are_flagged_and_neither_is_changed()
    {
        var vocal = Stem("Vocal - Full", "Key|A", "Key|Minor", "Meter|6/8");
        var drums = Stem("Drums", "Key|A", "Key|Minor", "Meter|4/4");
        var store = new MeterStore(Path.Combine(_root, "meters.json"));

        Assert.True(store.Settle(Song, _runner.MeterReadings(_root, Ledger(), index: null)));
        Pass(store);

        Assert.True(store.Of(Song)?.Contradicts);
        Assert.Equal(["Key|A", "Key|Minor", "Meter|6/8"], KeywordsOf(vocal));
        Assert.Equal(["Key|A", "Key|Minor", "Meter|4/4"], KeywordsOf(drums));
    }

    [Fact]
    public void A_meter_taken_off_one_stem_is_taken_off_the_others()
    {
        var drums = Stem("Drums", "Key|A", "Key|Minor", "Meter|4/4", "Meter|Odd bars");
        var store = new MeterStore(Path.Combine(_root, "meters.json"));

        store.Settle(
            Song,
            [new(Song, "k|Vocal", new HashSet<string> { "4/4", "Odd bars" }, new HashSet<string>())]
        );
        store.Settle(
            Song,
            [
                new(
                    Song,
                    "k|Vocal",
                    new HashSet<string> { "4/4" },
                    new HashSet<string> { "Odd bars" }
                ),
            ]
        );
        Pass(store);

        Assert.Equal(["Key|A", "Key|Minor", "Meter|4/4"], KeywordsOf(drums));
    }

    [Theory]
    [InlineData("3/4", true)]
    [InlineData("4/4", false)]
    public void Collection_7_follows_the_meter_tags_and_not_the_hand_mark(string meter, bool shown)
    {
        Stem("Vocal - Full", "Key|A", "Key|Minor");
        var store = new MeterStore(Path.Combine(_root, "meters.json"));

        store.Settle(
            Song,
            [new(Song, "k|Vocal", new HashSet<string> { meter }, new HashSet<string>())]
        );

        var assignments = _runner.Unbacked(
            _root,
            Ledger(),
            index: FolderInfo.Empty(_root),
            marksOf: song => store.MarksFor(song, [TrackMark.OddBars])
        );

        if (shown)
            Assert.Equal([(int)BrowserCollection.OddBars], Assert.Single(assignments).Collections);
        else
            Assert.Empty(assignments);
    }

    private void Pass(MeterStore store) =>
        _runner.Run(
            _root,
            Ledger(),
            ownedOf: _ => store.Wanted(Song),
            backKeys: false,
            retiredOf: _ => store.Retired(Song)
        );

    private string Stem(string preset, params string[] keywords)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, $"Song ({preset}).flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Comment = $"stemforge/0.4.0.0 | preset: {preset} | source: {Upload}";
            file.Save();
        }

        new FolderInfoWriter().Apply(
            _root,
            [new() { RelativePath = Path.GetFileName(path), Keywords = keywords }]
        );

        return path;
    }

    private IReadOnlyList<string> KeywordsOf(string path) =>
        new FolderInfoReader().Read(_root).TryGet(path, out var tags) ? tags.Keywords : [];

    private KeyLedger Ledger()
    {
        var jsonl = Path.Combine(_root, "ledger.jsonl");

        File.WriteAllText(
            jsonl,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-01T00:00:00+00:00",
                    song = Song,
                    artist = "A",
                    title = "T",
                    source_url = Upload,
                    verdict = new
                    {
                        key = "A minor",
                        confidence = "HIGH",
                        mode = "aeolian",
                    },
                }
            )
        );

        return TestLedger.From(jsonl);
    }
}
