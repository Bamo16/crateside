using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Genres;
using Crateside.Core.Imports;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Ledger;

public sealed class DecisionsImportTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(At);
    private readonly LedgerStore _ledger;
    private readonly CratesidePaths _paths;

    public DecisionsImportTests()
    {
        Directory.CreateDirectory(_root);

        _ledger = new LedgerStore(LedgerStore.InMemory, _time);
        _paths = new CratesidePaths
        {
            GenresFile = Path.Combine(_root, "genres.json"),
            TrackMarksFile = Path.Combine(_root, "track-marks.json"),
            PageChoicesFile = Path.Combine(_root, "page-choices.json"),
            QueueFile = Path.Combine(_root, "queue.json"),
            IntakeFile = Path.Combine(_root, "intake.json"),
        };
    }

    public void Dispose()
    {
        _ledger.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Every_genre_field_survives_the_import_and_the_file_is_renamed_not_deleted()
    {
        GenreRecord[] records =
        [
            new()
            {
                Song = "Fox Stevenson - Glue Gun",
                Learned = "Drum & Bass",
                LearnedFrom = "Fox Stevenson",
                Mine = ["Funk", "Meme"],
                Tagged = ["Drum & Bass"],
                RecordingMbid = "5b3a4c5d-0000-4000-8000-000000000000",
                Heard = ["Dance", "drum and bass", "electronic"],
                LookedUpAt = At.AddDays(-3),
            },
            new() { Song = "xg - woke up" },
            new()
            {
                Song = "yt9XdQw4w9W",
                Heard = ["pop"],
                LookedUpAt = At,
            },
        ];
        GenreJsonContext.Default.ListGenreRecord.WriteFile(_paths.GenresFile, [.. records]);

        var imported = Import();

        Assert.Equal(
            new DecisionFileImport("genres.json", 3, DecisionFileOutcome.Imported),
            Assert.Single(imported)
        );

        var read = new GenreStore(_ledger).Read();

        foreach (var expected in records)
        {
            var actual = read[expected.Song];

            Assert.Equal(
                expected with
                {
                    Heard = [],
                    Mine = [],
                    Tagged = [],
                },
                actual with
                {
                    Heard = [],
                    Mine = [],
                    Tagged = [],
                }
            );
            Assert.Equal(expected.Heard, actual.Heard);
            Assert.Equal(expected.Mine, actual.Mine);
            Assert.Equal(expected.Tagged, actual.Tagged);
        }

        Assert.Equal(3, read.Count);
        Assert.False(File.Exists(_paths.GenresFile));
        Assert.True(File.Exists($"{_paths.GenresFile}.imported-20261108"));
    }

    [Fact]
    public void Every_mark_survives_the_import()
    {
        TrackMarkJsonContext.Default.ListTrackMarkRecord.WriteFile(
            _paths.TrackMarksFile,
            [
                new TrackMarkRecord
                {
                    Song = "both marks",
                    Marks = [TrackMark.ChangesKey, TrackMark.OddBars],
                },
                new TrackMarkRecord { Song = "one mark", Marks = [TrackMark.OddBars] },
                new TrackMarkRecord { Song = "none left", Marks = [] },
            ]
        );

        Assert.Equal(
            new DecisionFileImport("track-marks.json", 3, DecisionFileOutcome.Imported),
            Assert.Single(Import())
        );

        var marks = new TrackMarkStore(_ledger);

        Assert.Equal([TrackMark.ChangesKey, TrackMark.OddBars], marks.Of("both marks"));
        Assert.Equal([TrackMark.OddBars], marks.Of("ONE MARK"));
        Assert.Empty(marks.Of("none left"));
        Assert.True(File.Exists($"{_paths.TrackMarksFile}.imported-20261108"));
    }

    [Fact]
    public void Every_page_choice_and_removed_source_survives_the_import_undone_ones_included()
    {
        List<PageChoicesRecord> file =
        [
            new(
                "IijgrkA_P50",
                [
                    new PageChoice
                    {
                        Url = "https://www.hooktheory.com/theorytab/view/billy-joel/piano-man",
                        Kind = PageChoiceKind.Added,
                        Page = new FetchedPage
                        {
                            Site = "Hooktheory",
                            Url = "https://www.hooktheory.com/theorytab/view/billy-joel/piano-man",
                            Heading = "Billy Joel - Piano Man",
                            Arrangement = "Chords",
                            Keys = [new PageKey("C major", "Chorus", 178.5)],
                            Note = "a note",
                            Tab = new HooktheoryTab
                            {
                                Contributors = ["someone"],
                                Sections =
                                [
                                    new TabSection("Verse", "I-V", 0.9, 0.5)
                                    {
                                        Keys = [new TabKey(0, "C major")],
                                        Chords = 4,
                                        Harmony = [new TabChord(0, "C:maj")],
                                        TrackKey = "IijgrkA_P50",
                                        SyncStart = 0.1,
                                        SyncEnd = 0.4,
                                    },
                                ],
                                AllKeys = "C major",
                                SyncedTrackKey = "IijgrkA_P50",
                                Coverage = 0.8,
                            },
                            Chosen = PageChoiceKind.Added,
                        },
                        At = At.AddDays(-2),
                    },
                    new PageChoice
                    {
                        Url = "https://www.hooktheory.com/theorytab/view/someone/else",
                        Kind = PageChoiceKind.Rejected,
                        Page = new FetchedPage
                        {
                            Site = "Hooktheory",
                            Url = "https://www.hooktheory.com/theorytab/view/someone/else",
                        },
                        At = At.AddDays(-2),
                        UndoneAt = At.AddDays(-1),
                    },
                ]
            )
            {
                Removed =
                [
                    new SourceRemoval("https://example.com/article/", At.AddHours(-4))
                    {
                        UndoneAt = At.AddHours(-2),
                    },
                    new SourceRemoval("web search", At.AddHours(-1)),
                ],
            },
            new("OnlyRemovals00", [])
            {
                Removed = [new SourceRemoval("https://example.org/x", At)],
            },
        ];
        LegacyDecisionJsonContext.Default.ListPageChoicesRecord.WriteFile(
            _paths.PageChoicesFile,
            file
        );

        Assert.Equal(
            new DecisionFileImport("page-choices.json", 5, DecisionFileOutcome.Imported),
            Assert.Single(Import())
        );

        var (choices, removals) = _ledger.Query(context =>
            (
                context.PageChoices.AsNoTracking().OrderBy(row => row.Id).ToList(),
                context.SourceRemovals.AsNoTracking().OrderBy(row => row.Id).ToList()
            )
        );

        Assert.Equal(
            Json(file[0].Choices),
            Json(choices.Where(row => row.TrackKey == "IijgrkA_P50").Select(row => row.ToChoice()))
        );
        Assert.Equal(
            Json(file[0].Removed!.Concat(file[1].Removed!)),
            Json(removals.Select(row => row.ToRemoval()))
        );
        Assert.Equal(
            ["IijgrkA_P50", "IijgrkA_P50", "OnlyRemovals00"],
            removals.Select(row => row.TrackKey)
        );

        var store = new PageChoiceStore(_ledger);

        Assert.Equal(PageChoiceKind.Added, Assert.Single(store.ActiveFor("IijgrkA_P50")).Kind);
        Assert.Equal(["web search"], store.RemovedFor("IijgrkA_P50"));
        Assert.Equal(["https://example.org/x"], store.RemovedFor("OnlyRemovals00"));
        Assert.True(File.Exists($"{_paths.PageChoicesFile}.imported-20261108"));
    }

    [Fact]
    public void A_second_run_finds_nothing_to_import()
    {
        GenreJsonContext.Default.ListGenreRecord.WriteFile(
            _paths.GenresFile,
            [new GenreRecord { Song = "a - b", Mine = ["Pop"] }]
        );
        Import();

        Assert.Empty(Import());
        Assert.Equal(["a - b"], new GenreStore(_ledger).Read().Keys);
    }

    [Fact]
    public void A_file_beside_a_table_that_already_holds_rows_is_left_alone()
    {
        new GenreStore(_ledger).Choose("kept", "Pop");
        GenreJsonContext.Default.ListGenreRecord.WriteFile(
            _paths.GenresFile,
            [new GenreRecord { Song = "other", Mine = ["Rock"] }]
        );

        Assert.Equal(
            new DecisionFileImport("genres.json", 0, DecisionFileOutcome.TableNotEmpty),
            Assert.Single(Import())
        );
        Assert.Equal(["kept"], new GenreStore(_ledger).Read().Keys);
        Assert.True(File.Exists(_paths.GenresFile));
    }

    [Fact]
    public void An_unreadable_file_is_left_in_place_and_nothing_is_imported()
    {
        File.WriteAllText(_paths.GenresFile, "{ not json");

        Assert.Equal(
            new DecisionFileImport("genres.json", 0, DecisionFileOutcome.Unreadable),
            Assert.Single(Import())
        );
        Assert.Empty(new GenreStore(_ledger).Read());
        Assert.Equal("{ not json", File.ReadAllText(_paths.GenresFile));
    }

    [Fact]
    public void A_second_import_on_the_same_day_does_not_overwrite_the_first_kept_file()
    {
        GenreJsonContext.Default.ListGenreRecord.WriteFile(
            _paths.GenresFile,
            [new GenreRecord { Song = "first", Mine = ["Pop"] }]
        );
        Import();

        _ledger.Save(context => context.Genres.RemoveRange(context.Genres));
        GenreJsonContext.Default.ListGenreRecord.WriteFile(
            _paths.GenresFile,
            [new GenreRecord { Song = "second", Mine = ["Rock"] }]
        );
        Import();

        Assert.Contains("first", File.ReadAllText($"{_paths.GenresFile}.imported-20261108"));
        Assert.Contains("second", File.ReadAllText($"{_paths.GenresFile}.imported-20261108-2"));
    }

    [Fact]
    public void Records_that_differ_only_in_case_import_as_one()
    {
        GenreJsonContext.Default.ListGenreRecord.WriteFile(
            _paths.GenresFile,
            [
                new GenreRecord { Song = "A - B", Mine = ["Pop"] },
                new GenreRecord { Song = "a - b", Mine = ["Pop"] },
            ]
        );

        Assert.Equal(1, Assert.Single(Import()).Rows);
    }

    [Fact]
    public void The_queue_file_imports_its_instructions_and_settings_and_converts_the_old_flag()
    {
        File.WriteAllText(
            _paths.QueueFile,
            """
            {
              "playlistId": "PL_user",
              "refreshMinutes": 7,
              "consumeOnArrival": true,
              "held": [ { "videoId": "gone", "reason": "wrong upload" } ],
              "tracks": [
                { "videoId": "withplan", "plan": { "presets": [ "vocal_full" ], "keepSource": true,
                    "addedSources": [ { "videoId": "glF85prh3e4", "label": "A Cappella", "type": null } ],
                    "adoptedSamples": [ { "label": "My edit" } ] },
                  "name": "Starjunk", "sourceUrl": "https://example.org/x",
                  "differentFrom": "Other - Song", "acceptedUpload": "up1",
                  "cutRequestedAt": "2026-10-01T10:00:00+00:00", "addedByUrl": true, "backfill": true,
                  "held": true, "holdReason": "x" },
                { "videoId": "oldflag", "instrumentalRelease": true },
                { "videoId": "empty" },
                { "videoId": "withplan", "name": "A duplicate" }
              ]
            }
            """
        );

        Assert.Equal(
            new DecisionFileImport("queue.json", 3, DecisionFileOutcome.Imported),
            Assert.Single(Import())
        );

        var instructions = new TrackInstructionStore(_ledger).Read().ToDictionary(i => i.TrackKey);
        var withPlan = instructions["withplan"];

        Assert.Equal(2, instructions.Count);
        Assert.Equal("Starjunk", withPlan.Name);
        Assert.Equal("https://example.org/x", withPlan.SourceUrl);
        Assert.Equal("Other - Song", withPlan.DifferentFrom);
        Assert.Equal("up1", withPlan.AcceptedUpload);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T10:00:00+00:00"), withPlan.CutRequestedAt);
        Assert.True(withPlan.AddedByUrl);
        Assert.True(withPlan.Backfill);
        Assert.Equal(["vocal_full"], withPlan.Plan!.Presets);
        Assert.True(withPlan.Plan.KeepSource);
        Assert.Equal("glF85prh3e4", Assert.Single(withPlan.Plan.AddedSources).TrackKey);
        Assert.Equal("My edit", Assert.Single(withPlan.Plan.AdoptedSamples).Label);
        Assert.True(instructions["oldflag"].Plan!.AsksTheSameAs(StemPlan.InstrumentalRelease));

        var settings = new PlaylistSettingsStore(
            _ledger,
            Options.Create(new PlaylistOptions { PlaylistId = "PL_config" })
        ).Read();

        Assert.Equal("PL_user", settings.PlaylistId);
        Assert.Equal(7, settings.RefreshMinutes);
        Assert.True(settings.ConsumeOnArrival);
        Assert.False(File.Exists(_paths.QueueFile));
        Assert.True(File.Exists($"{_paths.QueueFile}.imported-20261108"));
    }

    [Fact]
    public void A_queue_file_that_never_saved_a_setting_leaves_configuration_in_charge()
    {
        File.WriteAllText(
            _paths.QueueFile,
            """{ "tracks": [ { "videoId": "abc", "name": "Named" } ] }"""
        );

        Assert.Equal(1, Assert.Single(Import()).Rows);
        Assert.Equal(
            "PL_config",
            new PlaylistSettingsStore(
                _ledger,
                Options.Create(new PlaylistOptions { PlaylistId = "PL_config" })
            )
                .Read()
                .PlaylistId
        );
    }

    [Fact]
    public void Every_intake_field_survives_the_import_in_the_order_the_file_had()
    {
        File.WriteAllText(
            _paths.IntakeFile,
            """
            [
              { "videoId": "second", "title": "Second", "premiumOutlook": 1, "removalAttemptedAt": "2026-10-01T10:00:00+00:00",
                "removalFailure": { "reason": "forbidden", "canSucceedLater": true }, "removedEntryId": "PLI-9" },
              { "videoId": "file~25edb68200c05e82", "title": "Dave Wise - Lockjaw's Saga", "channelTitle": "Rare",
                "addedAt": "2026-11-06T20:19:06.3859908+00:00", "premiumOutlook": 0, "isUnavailable": true,
                "pageUrl": "https://example.org/p", "artist": "Dave Wise", "songTitle": "Lockjaw's Saga",
                "release": { "album": "Donkey Kong Country 2", "track": 6, "trackCount": 33, "year": 1995,
                             "yearText": "1995", "yearKind": "Release", "cover": "file~25edb68200c05e82.png" } },
              { "videoId": "second", "title": "A duplicate" }
            ]
            """
        );

        Assert.Equal(
            new DecisionFileImport("intake.json", 2, DecisionFileOutcome.Imported),
            Assert.Single(Import())
        );

        var read = new IntakeStore(_ledger).Read();

        Assert.Equal(["second", "file~25edb68200c05e82"], read.Select(record => record.TrackKey));
        Assert.Equal(PremiumOutlook.ArtTrack, read[0].PremiumOutlook);
        Assert.Equal(
            new RemovalFailure { Reason = "forbidden", CanSucceedLater = true },
            read[0].RemovalFailure
        );
        Assert.Equal("PLI-9", read[0].RemovedEntryId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T10:00:00+00:00"), read[0].RemovalAttemptedAt);
        Assert.True(read[1].IsUnavailable);
        Assert.Equal("Rare", read[1].ChannelTitle);
        Assert.Equal("Dave Wise", read[1].Artist);
        Assert.Equal("Donkey Kong Country 2", read[1].Release!.Album);
        Assert.Equal(YearKind.Release, read[1].Release!.YearKind);
        Assert.Equal(33, read[1].Release!.TrackCount);
        Assert.True(File.Exists($"{_paths.IntakeFile}.imported-20261108"));
    }

    [Fact]
    public void An_intake_file_beside_an_intake_that_already_holds_rows_is_left_alone()
    {
        new IntakeStore(_ledger).TakeIn(new IntakeRecord { TrackKey = "kept", Title = "Kept" });
        File.WriteAllText(_paths.IntakeFile, """[ { "videoId": "other", "title": "Other" } ]""");

        Assert.Equal(
            new DecisionFileImport("intake.json", 0, DecisionFileOutcome.TableNotEmpty),
            Assert.Single(Import())
        );
        Assert.Equal("kept", Assert.Single(new IntakeStore(_ledger).Read()).TrackKey);
        Assert.True(File.Exists(_paths.IntakeFile));
    }

    private IReadOnlyList<DecisionFileImport> Import() =>
        new DecisionsImport(_paths, _ledger, _time).Run();

    private static string Json<T>(T value) =>
        JsonSerializer.Serialize(value, DecisionTestJson.Options);
}

internal static class DecisionTestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
