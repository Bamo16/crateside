using System.Text.Json;
using Crateside.Core;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Quota;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class VerdictDrainerTests : IDisposable
{
    private const string VideoId = "backfill001";
    private const string PinnedSha = "pinned-build";

    private const string OldEvidence = """
        {"algorithms":{"librosa_ks":{"key":"A minor"},"crema":{"key":"A minor"},"ffmpeg_keydetect":{"key":"A minor"}},"versions":{"ffmpeg_sha256":"old-build"}}
        """;

    private const string FullEvidence = """
        {"algorithms":{"ffmpeg_keydetect":{"key":"A minor"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"C major"}},"versions":{"ffmpeg_sha256":"pinned-build"}}
        """;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly LedgerStore _store = TestLedger.Store();
    private readonly WorkingSet _set;
    private readonly JobStore _jobs;
    private readonly FakeSessions _sessions;
    private readonly FakeDownloads _downloads = new();
    private readonly RejudgeQueue _rejudges;
    private readonly VerdictDrainer _drainer;

    public VerdictDrainerTests()
    {
        Directory.CreateDirectory(_directory);

        _set = new WorkingSet(Path.Combine(_directory, "working"));
        _jobs = new JobStore(Path.Combine(_directory, "jobs.jsonl"));
        _sessions = new FakeSessions(() => File.ReadAllText(_set.EvidencePathFor(VideoId)));

        _rejudges = new RejudgeQueue(Path.Combine(_directory, "rejudge.json"), []);

        var paused = new PauseStore(Path.Combine(_directory, "paused.json"));
        var sources = new SourceIndex(_set, Path.Combine(_directory, "source-index.json"));
        var judge = Options.Create(
            new JudgeOptions
            {
                Environment = new() { ["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com" },
            }
        );

        _drainer = new VerdictDrainer(
            _store,
            null!,
            new VerdictRunner(
                _store,
                _set,
                sources,
                _sessions,
                new NoWindows(),
                new NoPages(),
                judge,
                new FakeTimeProvider(),
                NullLogger<VerdictRunner>.Instance
            ),
            new VerdictSelector(_jobs, _set),
            _jobs,
            new VerdictControl(paused, new FakeTimeProvider()),
            new QuotaGuard(new UnknownQuota(), new FakeTimeProvider()),
            _rejudges,
            _set,
            new EvidenceCompleter(_set, sources, new FakeDetectors()),
            new DownloadRunner(_downloads),
            paused,
            Options.Create(new CratesidePaths()),
            judge,
            new FakeTimeProvider()
        );
    }

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static WorkListsView Queue() =>
        WorkListsView.Compose(
            [
                new WorkItem
                {
                    VideoId = VideoId,
                    Title = "Artist - Song",
                    IsBackfill = true,
                },
            ],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction>()
        );

    private void Hold(string evidence, bool withSource)
    {
        var folder = _set.Prepare(VideoId);

        File.WriteAllText(_set.EvidencePathFor(VideoId), evidence);

        if (withSource)
            File.WriteAllText(Path.Combine(folder, "source.flac"), "audio");
    }

    private Task<StageTick> JudgeBackfillAsync() =>
        _drainer.JudgeNextAsync(
            Queue(),
            backfill: true,
            hold: null,
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task A_backfill_song_with_incomplete_evidence_is_completed_before_it_is_judged()
    {
        Hold(OldEvidence, withSource: true);

        await JudgeBackfillAsync();

        var judgedOn = _sessions.EvidenceSeen.First();

        Assert.All(_sessions.EvidenceSeen, seen => Assert.Equal(judgedOn, seen));

        Assert.Contains("madmom", judgedOn);
        Assert.Contains("essentia_edma", judgedOn);
        Assert.Contains(PinnedSha, judgedOn);
    }

    [Fact]
    public async Task A_backfill_song_whose_evidence_cannot_be_completed_is_failed_not_judged()
    {
        Hold(OldEvidence, withSource: false);

        var tick = await JudgeBackfillAsync();

        Assert.Empty(_sessions.EvidenceSeen);
        Assert.False(tick.IsSuccess);
        Assert.StartsWith("not judged:", tick.Error);
        Assert.True(_jobs.TryGet(VideoId, JobStage.Verdict, out var job));
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(1, job.Strikes);
    }

    [Fact]
    public async Task A_song_whose_evidence_is_complete_is_judged_as_it_stands()
    {
        Hold(FullEvidence, withSource: false);

        await JudgeBackfillAsync();

        Assert.NotEmpty(_sessions.EvidenceSeen);
        Assert.All(_sessions.EvidenceSeen, seen => Assert.Equal(FullEvidence, seen));
    }

    [Fact]
    public async Task A_re_judge_with_no_source_on_disk_downloads_it_again_before_the_judge_starts()
    {
        _store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                Song = "artist - song",
                Artist = "Artist",
                Title = "Song",
                SourceUrl = $"https://music.youtube.com/watch?v={VideoId}",
                Confidence = KeyConfidence.Medium,
                Center = new TonalCenter("A", DiatonicMode.Aeolian),
                Evidence = FullEvidence,
            }
        );
        _rejudges.Add("artist - song", DateTimeOffset.Parse("2026-10-02T00:00:00+00:00"), "test");

        await JudgeBackfillAsync();

        var download = Assert.Single(_downloads.Requests);

        Assert.Contains($"https://music.youtube.com/watch?v={VideoId}", download.Arguments);
        Assert.Contains(_set.Prepare(VideoId), download.Arguments);
        Assert.NotEmpty(_sessions.Prompts);
        Assert.DoesNotContain(VerdictRunner.NoAudioLine, _sessions.Prompts[0]);
    }

    [Fact]
    public async Task A_re_judge_whose_source_is_on_disk_downloads_nothing()
    {
        Hold(FullEvidence, withSource: true);
        _store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                Song = "artist - song",
                Artist = "Artist",
                Title = "Song",
                SourceUrl = $"https://music.youtube.com/watch?v={VideoId}",
                Confidence = KeyConfidence.Medium,
                Center = new TonalCenter("A", DiatonicMode.Aeolian),
                Evidence = FullEvidence,
            }
        );
        _rejudges.Add("artist - song", DateTimeOffset.Parse("2026-10-02T00:00:00+00:00"), "test");

        await JudgeBackfillAsync();

        Assert.Empty(_downloads.Requests);
        Assert.NotEmpty(_sessions.Prompts);
    }

    private sealed class FakeDownloads : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            var into = request.Arguments[request.Arguments.ToList().IndexOf("--output") + 1];
            var path = Path.Combine(into, "Artist - Song.flac");

            File.WriteAllText(path, "audio");

            return Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = 0,
                    StandardOutput = JsonSerializer.Serialize(
                        new[] { new { succeeded = true, path } }
                    ),
                    StandardError = string.Empty,
                }
            );
        }
    }

    private sealed class FakeSessions(Func<string> readEvidence) : IProcessRunner
    {
        public List<string> EvidenceSeen { get; } = [];

        public List<string> Prompts { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            EvidenceSeen.Add(readEvidence());
            Prompts.Add(request.StandardInput ?? string.Empty);

            return Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty,
                }
            );
        }
    }

    private sealed class UnknownQuota : IQuotaReader
    {
        public Task<QuotaSnapshot> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult(QuotaSnapshot.Unknown);
    }

    private sealed class NoWindows : IWindowDetector
    {
        public Task<EvidenceResult> DetectWindowAsync(
            string audioPath,
            RunWindow window,
            string evidencePath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<EvidenceResult> DetectWholeAsync(
            string audioPath,
            string evidencePath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class NoPages : ISourcePages
    {
        public Task<IReadOnlyList<SiteLookup>> FetchAsync(
            string artist,
            string title,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SiteLookup>>([]);
    }

    private sealed class FakeDetectors : IDetectorSet
    {
        public Task<string?> KeydetectSha256Async(CancellationToken ct = default) =>
            Task.FromResult<string?>(PinnedSha);

        public Task<EvidenceReading> ReadEvidenceAsync(
            string audioPath,
            CancellationToken ct = default
        ) => Task.FromResult<EvidenceReading>(new EvidenceReading.Read(FullEvidence));

        public Task<DetectionOutcome> DetectAsync(
            string audioPath,
            RunWindow? window,
            string evidencePath,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<(KeyReading Madmom, KeyReading Edma)> ReadBackersAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<KeydetectReading> ReadKeydetectAsync(
            string audioPath,
            RunWindow? window,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
