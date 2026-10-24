using Crateside.Core;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Pairing;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Evidence;

public sealed class EvidenceDrainerTests : IDisposable
{
    private const string JudgedId = "NSLYAT7sjAA";
    private const string NewId = "dQw4w9WgXcQ";
    private const string Judged = "rob dougan - furious angels";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly LedgerStore _store = new(LedgerStore.InMemory, new FakeTimeProvider());
    private readonly WorkingSet _set;
    private readonly EvidenceDrainer _drainer;

    public EvidenceDrainerTests()
    {
        _set = new WorkingSet(Path.Combine(_root, "working"));
        File.WriteAllText(Path.Combine(_set.Prepare(JudgedId), "source.flac"), "audio");

        var paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "staging"),
            LibraryFolder = Path.Combine(_root, "library"),
        };
        var detectors = new WritingDetectors();
        var jobs = new JobStore(Path.Combine(_root, "jobs.json"));

        _store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                Song = Judged,
                SourceUrl = $"https://music.youtube.com/watch?v={JudgedId}",
                Confidence = KeyConfidence.High,
                Centre = new TonalCentre("A", DiatonicMode.Aeolian),
                Evidence = """{"algorithms":{"ffmpeg_keydetect":{"key":"A minor"}}}""",
            }
        );

        _drainer = new EvidenceDrainer(
            _store,
            null!,
            new EvidenceRunner(_set, detectors),
            new EvidenceSelector(jobs, _set),
            new DetectorBackfill(
                _store,
                new SourceIndex(_set, Path.Combine(_root, "source-index.json")),
                detectors,
                paths
            ),
            jobs,
            Options.Create(paths)
        );
    }

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static WorkListsView Queue(params string[] videoIds) =>
        WorkListsView.Compose(
            [.. videoIds.Select(id => new WorkItem { VideoId = id, Title = $"Song {id}" })],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction>()
        );

    [Fact]
    public async Task With_nothing_queued_the_drainer_fills_a_judged_songs_missing_readings()
    {
        var tick = await _drainer.DrainAsync(Queue(), TestContext.Current.CancellationToken);

        Assert.Equal(StageOutcome.Ran, tick.Outcome);
        Assert.True(tick.IsSuccess);
        Assert.Equal(Judged, tick.Subject);
        Assert.Equal("added madmom, essentia_edma", tick.Note);
        Assert.False(tick.WakesOthers);
        Assert.Equal(3, _store.EvidenceFor(Judged)!.Algorithms.Count);
    }

    [Fact]
    public async Task New_intake_is_detected_before_any_backfill()
    {
        File.WriteAllText(Path.Combine(_set.Prepare(NewId), "source.flac"), "audio");

        var first = await _drainer.DrainAsync(Queue(NewId), TestContext.Current.CancellationToken);

        Assert.Equal($"Song {NewId}", first.Subject);
        Assert.True(first.WakesOthers);
        Assert.Single(_store.EvidenceFor(Judged)!.Algorithms);

        var second = await _drainer.DrainAsync(Queue(NewId), TestContext.Current.CancellationToken);

        Assert.Equal(Judged, second.Subject);
        Assert.Equal(3, _store.EvidenceFor(Judged)!.Algorithms.Count);
    }

    [Fact]
    public async Task Once_everything_is_filled_the_drainer_is_idle()
    {
        await _drainer.DrainAsync(Queue(), TestContext.Current.CancellationToken);

        var tick = await _drainer.DrainAsync(Queue(), TestContext.Current.CancellationToken);

        Assert.Equal(StageOutcome.Idle, tick.Outcome);
    }

    private sealed class WritingDetectors : IDetectorSet
    {
        public Task<string?> KeydetectSha256Async(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<EvidenceReading> ReadEvidenceAsync(
            string audioPath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<(KeyReading Madmom, KeyReading Edma)> ReadBackersAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) =>
            Task.FromResult<(KeyReading, KeyReading)>(
                (
                    new KeyReading.Read(MusicKey.FromString("A minor")!, 0.9),
                    new KeyReading.Read(MusicKey.FromString("C major")!, 0.7)
                )
            );

        public Task<KeydetectReading> ReadKeydetectAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public async Task<DetectionOutcome> DetectAsync(
            string audioPath,
            RunWindow? window,
            string evidencePath,
            CancellationToken ct = default
        )
        {
            await File.WriteAllTextAsync(evidencePath, "{}", ct);

            return new DetectionOutcome.Written(evidencePath);
        }
    }
}
