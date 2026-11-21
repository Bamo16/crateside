using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Edits;

public sealed class SourceAddTests : IDisposable
{
    private const string Mix = "luP5bpqhrcE";
    private const string Acapella = "glF85prh3e4";
    private const string MixSong = "kreayshawn - gucci gucci";
    private const string AcapellaSong = "kreayshawn - gucci gucci (acapella)";

    private static readonly TrackRowFacts Track = new()
    {
        TrackKey = Mix,
        Name = "Kreayshawn - Gucci Gucci",
        Song = MixSong,
        Plan = StemPlan.Default,
        Uploads = [Mix],
    };

    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
    );
    private readonly LedgerStore _ledger;
    private readonly TrackInstructionStore _instructions;
    private readonly IntakeStore _intake;
    private readonly RejudgeQueue _rejudges;
    private readonly CratesidePaths _paths;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public SourceAddTests()
    {
        _ledger = new LedgerStore(LedgerStore.InMemory, _time);
        _instructions = new TrackInstructionStore(_ledger);
        _intake = new IntakeStore(_ledger);
        _rejudges = new RejudgeQueue(Path.Combine(_root, "rejudge.json"));
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Mashup Samples"),
            StemsKeyIdentified = Path.Combine(_root, "identified"),
            StemsUnresolvedKey = Path.Combine(_root, "unresolved"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(_paths.LibraryFolder);
    }

    public void Dispose()
    {
        _ledger.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private SourceAdd Sources() =>
        new(
            _ledger,
            _instructions,
            _intake,
            _rejudges,
            new TrackRename(
                _instructions,
                new ListedStagedFiles(() => []),
                new FolderInfoWriter(),
                _paths,
                new LibraryFiles(_paths)
            ),
            new ListedStagedFiles(() => []),
            new LibraryFiles(_paths),
            new SourceProbe(new ProcessRunner()),
            _time
        );

    [Theory]
    [InlineData(SampleType.Instrumental, true)]
    [InlineData(SampleType.Vocal, false)]
    public void An_instrumental_asks_for_a_cut_so_its_guide_is_made_and_a_vocal_does_not(
        SampleType type,
        bool asked
    )
    {
        var source = new AddedSource
        {
            TrackKey = "bbbbbbbbbbb",
            Label = "Official Instrumental",
            Type = type,
        };

        Sources().Record(Track.TrackKey, Track.Plan, source);

        var recorded = Assert.Single(_instructions.Read());

        Assert.Equal(source, Assert.Single(recorded.Plan!.AddedSources));
        Assert.Equal(asked, recorded.CutRequestedAt is not null);
    }

    [Fact]
    public void Nothing_is_added_until_the_user_says_what_it_is()
    {
        var plan = Sources().PlanFor(Track, Acapella, "A Cappella", type: null);

        Assert.False(plan.IsPossible);
        Assert.NotNull(plan.Refusal);
    }

    [Fact]
    public void The_chosen_type_is_what_the_plan_records()
    {
        var plan = Sources().PlanFor(Track, Acapella, "A Cappella", SampleType.Vocal);

        Assert.True(plan.IsPossible);
        Assert.Null(plan.Fold);
        Assert.Equal(SampleType.Vocal, plan.Source?.Type);
    }

    [Fact]
    public async Task A_judged_song_folded_in_leaves_one_row_with_the_mixs_verdict_and_its_runs_in_the_ledger()
    {
        JudgeBoth();
        var file = InLibrary("Kreayshawn - Gucci Gucci (Acapella).flac");
        _instructions.AddByUrl(Acapella);
        _instructions.MarkBackfill(Acapella);
        _instructions.AcceptUpload(Acapella, Acapella);
        _rejudges.Add(AcapellaSong, _time.GetUtcNow(), "test");

        var plan = Sources().PlanFor(Track, Acapella, "Acapella", SampleType.Vocal);

        Assert.True(plan.IsPossible);
        Assert.Equal("Kreayshawn - Gucci Gucci (Acapella)", plan.Fold?.DisplayName);
        Assert.Equal([file], plan.Fold!.Files);
        Assert.Empty(plan.Fold.Renaming);

        // No probe: a probe would reach YouTube, and the file on disk needs no fetch.
        var outcome = await Sources()
            .ApplyAsync(
                Track,
                Acapella,
                "Acapella",
                SampleType.Vocal,
                ct: TestContext.Current.CancellationToken
            );

        Assert.True(outcome.Succeeded, outcome.Problem);
        Assert.True(File.Exists(file));

        var ledger = _ledger.Read();

        Assert.Equal(MixSong, ledger.SongFor(Acapella));
        Assert.Empty(ledger.UploadsOf(AcapellaSong));
        Assert.Equal(3, ledger.Runs.Count(run => run.Song == AcapellaSong && run.Restates is null));
        Assert.True(ledger.TryGetLatestBySong(AcapellaSong, out var kept));
        Assert.Equal(KeyConfidence.Withheld, kept.Confidence);

        var instruction = Assert.Single(_instructions.Read());

        Assert.Equal(Mix, instruction.TrackKey);
        Assert.Equal(Acapella, Assert.Single(instruction.Plan!.AddedSources).TrackKey);
        Assert.Empty(_rejudges.Read());

        // The acapella's playlist entry is still there, and still the row speaks for the mix.
        var row = Assert.Single(Rows(ledger, instruction, file).Rows);

        Assert.Equal(Mix, row.TrackKey);
        Assert.Equal(MixSong, row.Verdict?.Song);
        Assert.Equal(KeyConfidence.Medium, row.Verdict?.Confidence);
        Assert.True(row.IsAddedSource(Acapella));

        Jobs().Record(Mix, JobStage.Separation, JobStatus.Succeeded);

        Assert.False(
            new DownloadSelector(
                Jobs(),
                new WorkingSet(Path.Combine(_root, "working")),
                _paths,
                time: _time
            ).TryPickNext(Finished(row), out var fetched),
            $"fetched {fetched?.TrackKey}"
        );
    }

    [Fact]
    public async Task A_folded_file_whose_name_does_not_fit_takes_the_added_sources_name()
    {
        JudgeBoth();
        var file = InLibrary("Kreayshawn - Gucci Gucci (Acapella).flac");
        File.WriteAllText(StemFileName.AnalysisFor(file), "markers");

        var outcome = await Sources()
            .ApplyAsync(
                Track,
                Acapella,
                "A Cappella",
                SampleType.Vocal,
                ct: TestContext.Current.CancellationToken
            );

        Assert.True(outcome.Succeeded, outcome.Problem);

        var renamed = Path.Combine(
            _paths.LibraryFolder,
            "Kreayshawn - Gucci Gucci (A Cappella).flac"
        );

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(renamed));
        Assert.True(File.Exists(StemFileName.AnalysisFor(renamed)));
    }

    [Fact]
    public void A_track_with_no_verdict_has_no_song_to_fold_another_into()
    {
        JudgeBoth();

        var plan = Sources()
            .PlanFor(Track with { Song = null }, Acapella, "Acapella", SampleType.Vocal);

        Assert.False(plan.IsPossible);
        Assert.Contains("no verdict", plan.Refusal);
    }

    private JobStore Jobs() => new(Path.Combine(_root, "jobs.jsonl"), _time);

    /// <summary>The mix judged once; the acapella judged three times as its own song, then time passes.</summary>
    private void JudgeBoth()
    {
        Judge(MixSong, Mix, KeyConfidence.Medium);

        for (var i = 0; i < 3; i++)
            Judge(AcapellaSong, Acapella, KeyConfidence.Withheld);

        _time.Advance(TimeSpan.FromDays(30));
    }

    private void Judge(string song, string upload, KeyConfidence confidence)
    {
        _time.Advance(TimeSpan.FromHours(1));

        Assert.IsType<RecordOutcome.Recorded>(
            _ledger.Record(
                new RunRecord
                {
                    RecordedAt = _time.GetUtcNow(),
                    Song = song,
                    Artist = "Kreayshawn",
                    Title = song == MixSong ? "Gucci Gucci" : "Gucci Gucci (Acapella)",
                    SourceUrl = $"https://www.youtube.com/watch?v={upload}",
                    Confidence = confidence,
                    Center =
                        confidence is KeyConfidence.Withheld
                            ? null
                            : new TonalCenter("A#", DiatonicMode.Aeolian),
                }
            )
        );
    }

    /// <summary>A whole download in the library, its provenance naming the upload it came from.</summary>
    private string InLibrary(string name)
    {
        var path = Path.Combine(_paths.LibraryFolder, name);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using (var tagged = TagLib.File.Create(path))
        {
            tagged.Tag.Comment =
                $"stemforge/1.0 | source: https://www.youtube.com/watch?v={Acapella}";
            tagged.Save();
        }

        return path;
    }

    private TracksView Rows(KeyLedger ledger, TrackInstruction instruction, string file)
    {
        var instructions = new Dictionary<string, TrackInstruction> { [Mix] = instruction };
        var view = WorkListsView.Compose(
            [
                new WorkItem
                {
                    PlaylistItemId = "PLI-acapella",
                    TrackKey = Acapella,
                    Title = "Kreayshawn - Gucci Gucci (Acapella)",
                },
            ],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            instructions
        );

        return TracksView.Compose(
            view,
            ledger,
            [],
            new Dictionary<string, IReadOnlyList<StemOnDisk>>
            {
                [Mix] =
                [
                    new()
                    {
                        Path = Path.Combine(
                            _paths.LibraryFolder,
                            "Kreayshawn - Gucci Gucci (Vocal - Full).flac"
                        ),
                        Root = _paths.LibraryFolder,
                        Preset = "Vocal - Full",
                        SourceTrackKey = Mix,
                    },
                ],
                [Acapella] =
                [
                    new()
                    {
                        Path = file,
                        Root = _paths.LibraryFolder,
                        SourceTrackKey = Acapella,
                    },
                ],
            },
            _paths.LibraryFolder,
            Jobs(),
            instructions
        );
    }

    /// <summary>The mix as the download stage sees it: let go by the playlist, holding what the row holds.</summary>
    private static WorkListsView Finished(TrackRow row)
    {
        WorkItem item = new()
        {
            TrackKey = row.TrackKey,
            Title = row.Title,
            Plan = row.Plan,
            Inventory = row.Inventory,
        };

        return new WorkListsView
        {
            Items = [item],
            Outstanding = [],
            FullyProcessed = [item],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };
    }
}
