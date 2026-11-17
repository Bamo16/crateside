using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Imports;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Orchestration.Edits;

/// <summary>Naming many tracks from their tags through the one rename (#202).</summary>
public sealed class BulkRenameTests : IDisposable
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly TrackInstructionStore _settings;
    private readonly BulkRename _bulk;

    public BulkRenameTests()
    {
        Directory.CreateDirectory(_root);

        var paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };
        Directory.CreateDirectory(paths.StagingFolder);

        _settings = new TrackInstructionStore(_ledger);
        _bulk = new BulkRename(
            new TrackRename(
                _settings,
                new ListedStagedFiles(() => []),
                new FolderInfoWriter(),
                paths
            ),
            new ActionLog(Path.Combine(_root, "actions.jsonl"))
        );
    }

    public void Dispose()
    {
        _ledger.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static TrackRow Row(
        string key,
        string album,
        string artist,
        string title,
        string current = "ripped name"
    ) =>
        new()
        {
            TrackKey = key,
            Title = current,
            Release = new Release { Album = album },
            TagArtist = artist,
            TagTitle = title,
        };

    [Fact]
    public void The_game_is_the_album_before_its_colon()
    {
        Assert.Equal(
            "Donkey Kong Country 2",
            BulkRename.GameOf("Donkey Kong Country 2: Diddy's Kong Quest")
        );
        Assert.Equal("Chrono Trigger", BulkRename.GameOf("Chrono Trigger"));
        Assert.Null(BulkRename.GameOf(null));
    }

    [Fact]
    public void The_preview_names_each_track_from_its_tags_without_changing_anything()
    {
        var lines = _bulk.PreviewChecked(
            [Row("aaaaaaaaaaa", "Diddy's Kong Quest", "David Wise", "Stickerbush Symphony")],
            "{album} - {title}"
        );

        var line = Assert.Single(lines);
        Assert.Equal("Diddy's Kong Quest - Stickerbush Symphony", line.NewName);
        Assert.Empty(_settings.Read());
    }

    [Fact]
    public void A_game_pattern_drops_the_subtitle()
    {
        var lines = _bulk.PreviewChecked(
            [
                Row(
                    "aaaaaaaaaaa",
                    "Donkey Kong Country 2: Diddy's Kong Quest",
                    "Wise",
                    "Hot Head Bop"
                ),
            ],
            "{game} - {title}"
        );

        Assert.Equal("Donkey Kong Country 2 - Hot Head Bop", Assert.Single(lines).NewName);
    }

    [Fact]
    public void A_colon_in_a_tag_becomes_an_underscore_rather_than_a_refusal()
    {
        var lines = _bulk.PreviewChecked(
            [Row("aaaaaaaaaaa", "Game: Part 2", "Wise", "Theme")],
            "{album} - {title}"
        );

        Assert.Equal("Game_ Part 2 - Theme", Assert.Single(lines).NewName);
    }

    [Fact]
    public void A_track_missing_a_tag_in_the_pattern_is_skipped_with_the_reason()
    {
        var lines = _bulk.PreviewChecked(
            [new TrackRow { TrackKey = "aaaaaaaaaaa", Title = "From the playlist" }],
            "{album} - {title}"
        );

        var line = Assert.Single(lines);
        Assert.Null(line.NewName);
        Assert.Contains("no album", line.Problem);
    }

    [Fact]
    public void A_token_it_does_not_know_is_refused()
    {
        var lines = _bulk.PreviewChecked([Row("aaaaaaaaaaa", "A", "B", "C")], "{year} - {title}");

        Assert.Contains("{year}", Assert.Single(lines).Problem);
    }

    [Fact]
    public void Two_tracks_that_would_share_a_name_are_both_held_back()
    {
        var lines = _bulk.PreviewChecked(
            [Row("aaaaaaaaaaa", "A", "X", "Same"), Row("bbbbbbbbbbb", "A", "Y", "Same")],
            "{album} - {title}"
        );

        Assert.All(lines, line => Assert.Null(line.NewName));
    }

    [Fact]
    public void A_track_already_named_by_the_pattern_is_left_alone()
    {
        var lines = _bulk.PreviewChecked(
            [Row("aaaaaaaaaaa", "A", "X", "Song", current: "A - Song")],
            "{album} - {title}"
        );

        Assert.True(Assert.Single(lines).Unchanged);
    }

    private (BulkRename Bulk, FakeSets Sets, Func<int> StagedReads) Counting()
    {
        var sets = new FakeSets();
        var reads = 0;
        var paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };
        var bulk = new BulkRename(
            new TrackRename(
                _settings,
                new ListedStagedFiles(() =>
                {
                    reads++;

                    return [];
                }),
                new FolderInfoWriter(),
                paths,
                sets: sets
            ),
            new ActionLog(Path.Combine(_root, "actions.jsonl"))
        );

        return (bulk, sets, () => reads);
    }

    [Fact]
    public void Previewing_another_pattern_reads_no_files_and_no_sets_again()
    {
        var (bulk, sets, stagedReads) = Counting();
        var rows = Enumerable
            .Range(0, 700)
            .Select(i => Row($"track{i:D6}", "Album", "Artist", $"Song {i}"))
            .ToList();

        var bases = bulk.Prepare(rows);
        var readsToPrepare = stagedReads();
        var lookupsToPrepare = sets.Lookups.Count;

        var first = bulk.Preview(rows, "{album} - {title}", bases);
        var second = bulk.Preview(rows, "{artist} - {title}", bases);

        Assert.Equal(1, lookupsToPrepare);
        Assert.Equal(readsToPrepare, stagedReads());
        Assert.Equal(lookupsToPrepare, sets.Lookups.Count);
        Assert.Equal("Album - Song 5", first[5].NewName);
        Assert.Equal("Artist - Song 5", second[5].NewName);
    }

    [Fact]
    public void An_open_set_found_when_the_tracks_were_read_still_holds_the_track_back_in_the_preview()
    {
        var (bulk, sets, _) = Counting();
        sets.Users = ["Set"];
        sets.Open = new OpenSet(true, "Set");
        var rows = new[] { Row("aaaaaaaaaaa", "A", "X", "One") };
        var file = Path.Combine(_root, "Staging", "ripped name.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, []);
        var withFile = new BulkRename(
            new TrackRename(
                _settings,
                new ListedStagedFiles(() =>
                    [new StagedFile { Path = file, FileName = "ripped name.flac" }]
                ),
                new FolderInfoWriter(),
                new CratesidePaths
                {
                    StagingFolder = Path.Combine(_root, "Staging"),
                    LibraryFolder = Path.Combine(_root, "Library"),
                },
                sets: sets
            ),
            new ActionLog(Path.Combine(_root, "actions2.jsonl"))
        );

        var bases = withFile.Prepare(rows);
        sets.Open = OpenSet.NotRunning;

        var line = Assert.Single(withFile.Preview(rows, "{artist} - {title}", bases));

        Assert.Contains("open in Ableton", line.Problem);
    }

    [Fact]
    public void A_file_already_called_the_new_name_is_found_in_the_preview_from_the_listing_taken_earlier()
    {
        var staging = Path.Combine(_root, "Staging");
        var file = Path.Combine(staging, "ripped name.flac");
        File.WriteAllBytes(file, []);
        File.WriteAllBytes(Path.Combine(staging, "X - One.flac"), []);
        var bulk = new BulkRename(
            new TrackRename(
                _settings,
                new ListedStagedFiles(() =>
                    [new StagedFile { Path = file, FileName = "ripped name.flac" }]
                ),
                new FolderInfoWriter(),
                new CratesidePaths
                {
                    StagingFolder = staging,
                    LibraryFolder = Path.Combine(_root, "Library"),
                }
            ),
            new ActionLog(Path.Combine(_root, "actions3.jsonl"))
        );
        var rows = new[] { Row("aaaaaaaaaaa", "A", "X", "One") };

        var line = Assert.Single(bulk.Preview(rows, "{artist} - {title}", bulk.Prepare(rows)));

        Assert.Contains("already in", line.Problem);
    }

    [Fact]
    public void The_light_preview_names_every_row_from_its_tags_and_marks_it_pending()
    {
        var lines = _bulk.Preview(
            [Row("aaaaaaaaaaa", "A", "X", "One"), Row("bbbbbbbbbbb", "A", "X", "Two")],
            "{artist} - {title}"
        );

        Assert.Equal(["X - One", "X - Two"], lines.Select(line => line.NewName));
        Assert.All(lines, line => Assert.True(line.Pending));
    }

    [Fact]
    public void The_checked_preview_is_not_pending()
    {
        var lines = _bulk.PreviewChecked(
            [Row("aaaaaaaaaaa", "A", "X", "One")],
            "{artist} - {title}"
        );

        Assert.All(lines, line => Assert.False(line.Pending));
    }

    [Fact]
    public void Applying_records_each_name_the_way_the_song_pages_rename_does()
    {
        var lines = _bulk.PreviewChecked(
            [Row("aaaaaaaaaaa", "A", "X", "One"), Row("bbbbbbbbbbb", "A", "X", "Two")],
            "{artist} - {title}"
        );

        var result = _bulk.Apply(lines);

        Assert.Equal(2, result.Renamed);
        Assert.Equal(
            ["X - One", "X - Two"],
            _settings.Read().Select(instruction => instruction.Name).Order()
        );
    }
}
