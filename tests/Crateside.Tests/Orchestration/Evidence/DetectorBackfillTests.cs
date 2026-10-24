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
    private const string VideoId = "NSLYAT7sjAA";
    private const string Song = "rob dougan - furious angels";

    private const string Before =
        """{"input":"a.flac","algorithms":{"ffmpeg_keydetect":{"key":"A minor","r":0.8}}}""";

    private const string Complete =
        """{"algorithms":{"ffmpeg_keydetect":{"key":"A minor"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"C major"}}}""";

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

        File.WriteAllText(Path.Combine(set.Prepare(VideoId), "source.flac"), "audio");

        return new(
            _store,
            new SourceIndex(set, Path.Combine(_root, "source-index.json")),
            _detectors,
            Paths
        );
    }

    private void Judge(string evidence) =>
        Assert.IsType<RecordOutcome.Recorded>(
            _store.Record(
                new RunRecord
                {
                    RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                    Song = Song,
                    SourceUrl = $"https://music.youtube.com/watch?v={VideoId}",
                    Confidence = KeyConfidence.High,
                    Centre = new TonalCentre("A", DiatonicMode.Aeolian),
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
        Judge("""{"algorithms":{"madmom":{"key":"A minor"},"essentia_edma":{"key":"A minor"}}}""");

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
            Paths
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
            Path.Combine(_root, "working", VideoId, "source.flac"),
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

    private static string ReadBy(
        string sha256,
        string key = "A minor",
        string chords = """["Am"]"""
    ) =>
        $$$"""{"algorithms":{"ffmpeg_keydetect":{"key":"{{{key}}}"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"A minor"}},"chords":{{{chords}}},"versions":{"ffmpeg_sha256":"{{{sha256}}}"}}""";

    private sealed class FakeDetectors : IDetectorSet
    {
        public bool Fail { get; set; }

        public string? Sha256 { get; set; }

        public string? Fresh { get; set; }

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
                        []
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
