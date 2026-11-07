using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Verdict;
using Microsoft.EntityFrameworkCore;
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
            FamiliarityFile = Path.Combine(_root, "familiarity.json"),
            TrackMarksFile = Path.Combine(_root, "track-marks.json"),
            PageChoicesFile = Path.Combine(_root, "page-choices.json"),
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
                Genre = "Drum & Bass",
                IsSettled = true,
                LearnedFrom = "Fox Stevenson",
                Suggested = "Drum & Bass",
                Heard = ["Dance", "drum and bass", "electronic"],
                LookedUpAt = At.AddDays(-3),
            },
            new()
            {
                Song = "xg - woke up",
                Genre = null,
                IsSettled = true,
            },
            new()
            {
                Song = "yt9XdQw4w9W",
                Suggested = "Pop",
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

            Assert.Equal(expected with { Heard = [] }, actual with { Heard = [] });
            Assert.Equal(expected.Heard, actual.Heard);
        }

        Assert.Equal(3, read.Count);
        Assert.False(File.Exists(_paths.GenresFile));
        Assert.True(File.Exists($"{_paths.GenresFile}.imported-20261108"));
    }

    [Fact]
    public void Every_familiarity_field_survives_the_import()
    {
        FamiliarityRecord[] records =
        [
            new()
            {
                Song = "Moe Shop - Love Taste",
                Artist = "Moe Shop",
                ArtistFans = 17_522,
                SongRank = 167_703,
                LookedUpAt = At.AddHours(-5),
                LookupVersion = 2,
                Settled = "Obscure",
            },
            new() { Song = "unmeasured", Settled = "Mainstream" },
            new() { Song = "not found", LookedUpAt = At },
        ];
        FamiliarityJsonContext.Default.ListFamiliarityRecord.WriteFile(
            _paths.FamiliarityFile,
            [.. records]
        );

        Assert.Equal(
            new DecisionFileImport("familiarity.json", 3, DecisionFileOutcome.Imported),
            Assert.Single(Import())
        );

        var read = new FamiliarityStore(_ledger).Read();

        foreach (var expected in records)
            Assert.Equal(expected, read[expected.Song]);

        Assert.True(File.Exists($"{_paths.FamiliarityFile}.imported-20261108"));
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
            [
                new GenreRecord
                {
                    Song = "a - b",
                    Genre = "Pop",
                    IsSettled = true,
                },
            ]
        );
        Import();

        Assert.Empty(Import());
        Assert.Equal(["a - b"], new GenreStore(_ledger).Read().Keys);
    }

    [Fact]
    public void A_file_beside_a_table_that_already_holds_rows_is_left_alone()
    {
        new GenreStore(_ledger).Settle("kept", "Pop");
        GenreJsonContext.Default.ListGenreRecord.WriteFile(
            _paths.GenresFile,
            [
                new GenreRecord
                {
                    Song = "other",
                    Genre = "Rock",
                    IsSettled = true,
                },
            ]
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
            [
                new GenreRecord
                {
                    Song = "first",
                    Genre = "Pop",
                    IsSettled = true,
                },
            ]
        );
        Import();

        _ledger.Save(context => context.Genres.RemoveRange(context.Genres));
        GenreJsonContext.Default.ListGenreRecord.WriteFile(
            _paths.GenresFile,
            [
                new GenreRecord
                {
                    Song = "second",
                    Genre = "Rock",
                    IsSettled = true,
                },
            ]
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
                new GenreRecord
                {
                    Song = "A - B",
                    Genre = "Pop",
                    IsSettled = true,
                },
                new GenreRecord
                {
                    Song = "a - b",
                    Genre = "Pop",
                    IsSettled = true,
                },
            ]
        );

        Assert.Equal(1, Assert.Single(Import()).Rows);
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
