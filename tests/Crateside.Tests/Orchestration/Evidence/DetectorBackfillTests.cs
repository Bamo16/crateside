using Crateside.Core;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Pairing;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Evidence;

public sealed class DetectorBackfillTests : IDisposable
{
    private const string TrackKey = "NSLYAT7sjAA";
    private const string Song = "rob dougan - furious angels";

    private const string Before =
        """{"input":"a.flac","algorithms":{"ffmpeg_keydetect":{"key":"A minor","r":0.8}},"key_lifts":[]}""";

    private const string Complete =
        """{"algorithms":{"ffmpeg_keydetect":{"key":"A minor"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"C major"}},"key_lifts":[]}""";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly LedgerStore _store = new(LedgerStore.InMemory, new FakeTimeProvider());
    private readonly FakeDetectors _detectors = new();

    public DetectorBackfillTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private CratesidePaths Paths =>
        new()
        {
            StagingFolder = Path.Combine(_root, "staging"),
            LibraryFolder = Path.Combine(_root, "library"),
        };

    private DetectorBackfill Backfill()
    {
        WorkingSet set = new(Path.Combine(_root, "working"));

        File.WriteAllText(Path.Combine(set.Prepare(TrackKey), "source.flac"), "audio");

        return new(
            _store,
            new SourceIndex(set, Path.Combine(_root, "source-index.json")),
            _detectors,
            Paths,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-10-25T00:00:00+00:00"))
        );
    }

    private void Judge(string? evidence) =>
        Assert.IsType<RecordOutcome.Recorded>(
            _store.Record(
                new RunRecord
                {
                    RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                    Song = Song,
                    SourceUrl = $"https://music.youtube.com/watch?v={TrackKey}",
                    Confidence = KeyConfidence.High,
                    Center = new TonalCenter("A", DiatonicMode.Aeolian),
                    Evidence = evidence,
                }
            )
        );

    [Fact]
    public async Task The_readings_join_the_existing_run_without_recording_another()
    {
        Judge(Before);

        var backfill = Backfill();
        var result = await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );

        Assert.Equal([MadmomKeyCnn.Name, EdmaKeyEstimator.Name], result.Added);
        Assert.Empty(result.Failed);
        Assert.Single(_store.Read().Runs);
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
            _store.Read().Runs[0].Timestamp
        );

        var evidence = _store.EvidenceFor(Song)!;

        Assert.Equal(
            ["ffmpeg keydetect", "madmom", "Essentia edma"],
            evidence.Algorithms.Select(algorithm => algorithm.Name)
        );
        Assert.Equal("A minor", evidence.Algorithms[0].Key);
        Assert.Equal("A minor", evidence.Algorithms[1].Key);
        Assert.Equal("C major", evidence.Algorithms[2].Key);
        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_song_missing_a_detector_is_picked_with_what_it_lacks()
    {
        Judge(Before);

        var target = (await Backfill().NextAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(Song, target.Song);
        Assert.Equal([MadmomKeyCnn.Name, EdmaKeyEstimator.Name], target.Detectors);
    }

    [Fact]
    public async Task A_song_missing_keydetect_gets_its_reading_and_chords()
    {
        Judge(
            """{"algorithms":{"madmom":{"key":"A minor"},"essentia_edma":{"key":"A minor"}},"key_lifts":[]}"""
        );

        var backfill = Backfill();
        var result = await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );

        Assert.Equal([KeydetectFilter.Name], result.Added);

        var evidence = _store.EvidenceFor(Song)!;

        Assert.Equal(
            "A minor",
            evidence.Algorithms.Single(algorithm => algorithm.Name == "ffmpeg keydetect").Key
        );
        Assert.Equal(["Am", "G"], evidence.Chords);
    }

    [Fact]
    public async Task A_song_that_has_every_reading_is_left_alone()
    {
        Judge(Complete);

        Assert.Null(await Backfill().NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_run_with_no_evidence_at_all_is_picked_for_all_three_and_filled()
    {
        Judge(null);

        var backfill = Backfill();
        var target = await backfill.NextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [KeydetectFilter.Name, MadmomKeyCnn.Name, EdmaKeyEstimator.Name, KeyLifts.Name],
            target!.Detectors
        );

        var result = await backfill.ReadAsync(target, TestContext.Current.CancellationToken);

        Assert.Empty(result.Failed);
        Assert.Equal(3, _store.EvidenceFor(Song)!.Algorithms.Count);
        Assert.Single(_store.Read().Runs);
    }

    [Fact]
    public async Task Evidence_never_scanned_for_key_lifts_gets_them_added_and_nothing_else()
    {
        Judge(
            """{"algorithms":{"ffmpeg_keydetect":{"key":"A minor"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"C major"}}}"""
        );

        var backfill = Backfill();
        var target = (await backfill.NextAsync(TestContext.Current.CancellationToken))!;
        var result = await backfill.ReadAsync(target, TestContext.Current.CancellationToken);

        Assert.Equal([KeyLifts.Name], target.Detectors);
        Assert.Equal([KeyLifts.Name], result.Added);
        Assert.Contains(
            "\"key_lifts\":[{\"at\":124,\"length\":65,\"shift\":1,\"strength\":0.949,\"fit\":0.2785}]",
            _store.LatestEvidenceJson()[Song]
        );
        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Missing_key_lifts_do_not_hold_up_judging()
    {
        Assert.True(DetectorBackfill.IsComplete(Complete.Replace(",\"key_lifts\":[]", ""), null));
    }

    [Fact]
    public async Task A_song_with_no_source_on_disk_is_skipped()
    {
        Judge(Before);

        var backfill = new DetectorBackfill(
            _store,
            new SourceIndex(
                new WorkingSet(Path.Combine(_root, "empty")),
                Path.Combine(_root, "i.json")
            ),
            _detectors,
            Paths,
            new FakeTimeProvider()
        );

        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_reading_that_fails_is_not_retried_until_the_app_restarts()
    {
        Judge(Before);
        _detectors.Fail = true;

        var backfill = Backfill();
        var result = await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("too short", result.Failed[MadmomKeyCnn.Name]);
        Assert.Empty(result.Added);
        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            _store.EvidenceFor(Song)!.Algorithms,
            algorithm => algorithm.Name == "madmom"
        );
        Assert.NotNull(await Backfill().NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_changed_source_is_tried_again_after_a_failure()
    {
        Judge(Before);
        _detectors.Fail = true;

        var backfill = Backfill();

        await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );
        File.SetLastWriteTimeUtc(
            Path.Combine(_root, "working", TrackKey, "source.flac"),
            DateTime.UtcNow.AddHours(1)
        );

        Assert.NotNull(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Attaching_keeps_a_reading_already_there_and_every_other_field()
    {
        var edited = DetectorBackfill.Attach(
            """{"input":"a.flac","chords":["A"],"algorithms":{"madmom":{"key":"D major"}}}""",
            DetectorSet.Part(
                null,
                new KeyReading.Read(MusicKey.FromString("A minor")!, 0.5),
                new KeyReading.Read(MusicKey.FromString("C major")!, 0.4)
            )
        );

        Assert.Contains("\"input\":\"a.flac\"", edited);
        Assert.Contains("\"chords\":[\"A\"]", edited);
        Assert.Contains("\"madmom\":{\"key\":\"D major\"}", edited);
        Assert.Contains("\"essentia_edma\":{\"key\":\"C major\",\"confidence\":0.4}", edited);
    }

    [Fact]
    public async Task Evidence_another_build_read_is_picked_to_re_read_all_three()
    {
        Judge(ReadBy("old"));
        _detectors.Sha256 = "new";

        var target = (await Backfill().NextAsync(TestContext.Current.CancellationToken))!;

        Assert.True(target.Rereads);
        Assert.Equal(
            [KeydetectFilter.Name, MadmomKeyCnn.Name, EdmaKeyEstimator.Name],
            target.Detectors
        );
    }

    [Fact]
    public async Task Evidence_the_current_build_read_is_left_alone()
    {
        Judge(ReadBy("NEW"));
        _detectors.Sha256 = "new";

        Assert.Null(await Backfill().NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_missing_reading_is_added_before_the_evidence_is_re_read()
    {
        Judge(Before);
        _detectors.Sha256 = "new";

        var target = (await Backfill().NextAsync(TestContext.Current.CancellationToken))!;

        Assert.False(target.Rereads);
    }

    [Fact]
    public async Task A_re_read_replaces_the_readings_and_keeps_the_run_and_its_verdict()
    {
        Judge(ReadBy("old"));
        _detectors.Sha256 = "new";
        _detectors.Fresh = ReadBy("new", key: "C major", chords: """["C","G"]""");

        var backfill = Backfill();
        var result = await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result.Failed);
        Assert.Equal(3, result.Added.Count);

        var run = Assert.Single(_store.Read().Runs);
        var evidence = _store.EvidenceFor(Song)!;

        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"), run.Timestamp);
        Assert.Equal(KeyConfidence.High, run.Confidence);
        Assert.Equal(
            "C major",
            evidence.Algorithms.Single(algorithm => algorithm.Name == "ffmpeg keydetect").Key
        );
        Assert.Equal(["C", "G"], evidence.Chords);
        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_re_read_marks_the_evidence_as_read_after_the_verdict()
    {
        Judge(ReadBy("old"));
        _detectors.Sha256 = "new";
        _detectors.Fresh = ReadBy("new", key: "C major", chords: """["C","G"]""");

        var backfill = Backfill();
        await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("new", _store.EvidenceFor(Song)!.LateReading?.Sha256);
        Assert.NotNull(_store.EvidenceFor(Song)!.LateReading?.At);
    }

    [Fact]
    public async Task Readings_added_to_a_finished_run_mark_it_as_read_after_the_verdict()
    {
        Judge(Before);

        var backfill = Backfill();
        await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(_store.EvidenceFor(Song)!.LateReading);
    }

    [Fact]
    public async Task A_run_re_read_before_the_marker_existed_is_marked_once_from_its_build_and_time()
    {
        Judge(ReadBy("NEW"));
        _detectors.Sha256 = "new";

        Assert.Null(await Backfill().NextAsync(TestContext.Current.CancellationToken));

        var late = _store.EvidenceFor(Song)!.LateReading;

        Assert.NotNull(late);
        Assert.Null(late.At);
        Assert.Equal("NEW", late.Sha256);
    }

    [Fact]
    public async Task A_run_judged_on_the_current_build_is_not_marked()
    {
        _store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-24T23:00:00-05:00"),
                Song = Song,
                SourceUrl = $"https://music.youtube.com/watch?v={TrackKey}",
                Confidence = KeyConfidence.High,
                Center = new TonalCenter("A", DiatonicMode.Aeolian),
                Evidence = ReadBy("new"),
            }
        );
        _detectors.Sha256 = "new";

        Assert.Null(await Backfill().NextAsync(TestContext.Current.CancellationToken));
        Assert.Null(_store.EvidenceFor(Song)!.LateReading);
    }

    [Fact]
    public void The_note_names_the_build_when_it_is_known()
    {
        Assert.Equal(
            "Read on keydetect 1.2.1, after this verdict.",
            new LateReading(null, "x").Note("1.2.1")
        );
        Assert.Equal("Read after this verdict.", new LateReading(null, "x").Note(null));
    }

    [Fact]
    public async Task A_re_read_where_a_detector_reads_no_key_keeps_the_earlier_readings()
    {
        Judge(ReadBy("old"));
        _detectors.Sha256 = "new";
        _detectors.Fresh =
            """{"algorithms":{"ffmpeg_keydetect":{"error":"silent"},"madmom":{"key":"C major"}},"versions":{"ffmpeg_sha256":"new"}}""";

        var backfill = Backfill();
        var result = await backfill.ReadAsync(
            (await backfill.NextAsync(TestContext.Current.CancellationToken))!,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result.Added);
        Assert.Single(result.Failed);
        Assert.Equal(
            "A minor",
            _store
                .EvidenceFor(Song)!
                .Algorithms.Single(algorithm => algorithm.Name == "ffmpeg keydetect")
                .Key
        );
        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Refreshing_replaces_each_fresh_reading_and_keeps_every_other_field()
    {
        var refreshed = DetectorBackfill.Refreshed(
            """{"bpm_rough":120,"chords":["A"],"algorithms":{"crema":{"key":"A minor"},"madmom":{"key":"A minor"}},"versions":{"ffmpeg_sha256":"old"}}""",
            """{"chords":["C"],"algorithms":{"madmom":{"key":"C major"}},"versions":{"ffmpeg_sha256":"new"}}"""
        );

        Assert.Equal(
            """{"bpm_rough":120,"chords":["C"],"algorithms":{"crema":{"key":"A minor"},"madmom":{"key":"C major"}},"versions":{"ffmpeg_sha256":"new"}}""",
            refreshed
        );
    }

    [Fact]
    public async Task A_run_with_chords_but_no_chord_times_gets_the_times_and_nothing_else()
    {
        Judge(ReadBy("new", chords: """["Am","G"]""", starts: null));
        _detectors.Sha256 = "new";
        _detectors.ChordStarts = [0, 4.5];

        var backfill = Backfill();
        var target = (await backfill.NextAsync(TestContext.Current.CancellationToken))!;
        var before = _store.LatestEvidenceJson()[Song];
        var result = await backfill.ReadAsync(target, TestContext.Current.CancellationToken);

        Assert.Equal([DetectorBackfill.ChordTimes], target.Detectors);
        Assert.False(target.Rereads);
        Assert.Equal([DetectorBackfill.ChordTimes], result.Added);

        var after = _store.LatestEvidenceJson()[Song];

        Assert.Equal(before[..^1] + ",\"chord_starts\":[0,4.5]}", after);
        Assert.Equal(Song, Assert.Single(_store.Read().Runs).Song);
        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_run_another_build_read_is_re_read_rather_than_given_chord_times()
    {
        Judge(ReadBy("old", chords: """["Am","G"]""", starts: null));
        _detectors.Sha256 = "new";

        Assert.True((await Backfill().NextAsync(TestContext.Current.CancellationToken))!.Rereads);
    }

    [Fact]
    public async Task A_build_that_prints_no_chord_times_is_tried_once_and_leaves_the_evidence_alone()
    {
        Judge(ReadBy("new", chords: """["Am","G"]""", starts: null));
        _detectors.Sha256 = "new";

        var backfill = Backfill();
        var target = (await backfill.NextAsync(TestContext.Current.CancellationToken))!;
        var before = _store.LatestEvidenceJson()[Song];
        var result = await backfill.ReadAsync(target, TestContext.Current.CancellationToken);

        Assert.Contains(DetectorBackfill.ChordTimes, result.Failed.Keys);
        Assert.Equal(before, _store.LatestEvidenceJson()[Song]);
        Assert.Null(await backfill.NextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Times_for_a_different_number_of_chords_are_not_attached()
    {
        Judge(ReadBy("new", chords: """["Am","G","C"]""", starts: null));
        _detectors.Sha256 = "new";
        _detectors.ChordStarts = [0, 4.5];

        var backfill = Backfill();
        var target = (await backfill.NextAsync(TestContext.Current.CancellationToken))!;
        var before = _store.LatestEvidenceJson()[Song];
        var result = await backfill.ReadAsync(target, TestContext.Current.CancellationToken);

        Assert.Contains(DetectorBackfill.ChordTimes, result.Failed.Keys);
        Assert.Equal(before, _store.LatestEvidenceJson()[Song]);
    }

    private static string ReadBy(
        string sha256,
        string key = "A minor",
        string chords = """["Am"]""",
        string? starts = "[0]"
    ) =>
        $$$"""{"algorithms":{"ffmpeg_keydetect":{"key":"{{{key}}}"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"A minor"}},"chords":{{{chords}}}{{{(starts is null ? string.Empty : $",\"chord_starts\":{starts}")}}},"key_lifts":[],"versions":{"ffmpeg_sha256":"{{{sha256}}}"}}""";

    private sealed class FakeDetectors : IDetectorSet
    {
        public bool Fail { get; set; }

        public string? Sha256 { get; set; }

        public string? Fresh { get; set; }

        public IReadOnlyList<double> ChordStarts { get; set; } = [];

        public Task<string?> KeydetectSha256Async(CancellationToken ct = default) =>
            Task.FromResult(Sha256);

        public Task<EvidenceReading> ReadEvidenceAsync(
            string audioPath,
            CancellationToken ct = default
        ) =>
            Task.FromResult<EvidenceReading>(
                Fail ? new EvidenceReading.Failed("too short") : new EvidenceReading.Read(Fresh!)
            );

        public Task<(KeyReading Madmom, KeyReading Edma)> ReadBackersAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) =>
            Task.FromResult<(KeyReading, KeyReading)>(
                Fail
                    ? (new KeyReading.Failed("too short"), new KeyReading.Failed("too short"))
                    : (
                        new KeyReading.Read(MusicKey.FromString("A minor")!, 0.9),
                        new KeyReading.Read(MusicKey.FromString("C major")!, 0.7)
                    )
            );

        public Task<KeyLiftReading> ReadKeyLiftsAsync(
            string audioPath,
            CancellationToken ct = default
        ) =>
            Task.FromResult<KeyLiftReading>(
                Fail
                    ? new KeyLiftReading.Failed("too short")
                    : new KeyLiftReading.Read([new KeyLift(124, 65, 1, 0.949, 0.2785)])
            );

        public Task<KeydetectReading> ReadKeydetectAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) =>
            Task.FromResult<KeydetectReading>(
                Fail
                    ? new KeydetectReading.Failed("too short")
                    : new KeydetectReading.Read(
                        MusicKey.FromString("A minor")!,
                        0.8,
                        ["Am", "G"],
                        [],
                        ChordStarts: ChordStarts
                    )
            );

        public Task<DetectionOutcome> DetectAsync(
            string audioPath,
            RunWindow? window,
            string evidencePath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
