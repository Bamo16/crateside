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
    private const string TrackKey = "backfill001";
    private const string PinnedSha = "pinned-build";

    private const string OldEvidence = """
        {"algorithms":{"librosa_ks":{"key":"A minor"},"crema":{"key":"A minor"},"ffmpeg_keydetect":{"key":"A minor"}},"versions":{"ffmpeg_sha256":"old-build"}}
        """;

    private const string FullEvidence = """
        {"algorithms":{"ffmpeg_keydetect":{"key":"A minor"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"C major"}},"versions":{"ffmpeg_sha256":"pinned-build"}}
        """;

    private const string AgreedEvidence = """
        {"algorithms":{"ffmpeg_keydetect":{"key":"A minor"},"madmom":{"key":"A minor"},"essentia_edma":{"key":"A minor"}},"versions":{"ffmpeg_sha256":"pinned-build"}}
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
    private readonly VerdictControl _control;
    private readonly PauseStore _paused;
    private readonly SourceIndex _sources;

    private readonly FakeTimeProvider _time = new(
        DateTimeOffset.Parse("2026-10-30T15:36:00+00:00")
    );

    public VerdictDrainerTests()
    {
        Directory.CreateDirectory(_directory);

        _set = new WorkingSet(Path.Combine(_directory, "working"));
        _jobs = new JobStore(Path.Combine(_directory, "jobs.jsonl"));
        _sessions = new FakeSessions(() => File.ReadAllText(_set.EvidencePathFor(TrackKey)));

        _rejudges = new RejudgeQueue(Path.Combine(_directory, "rejudge.json"));

        _paused = new PauseStore(Path.Combine(_directory, "paused.json"));
        _sources = new SourceIndex(_set, Path.Combine(_directory, "source-index.json"));
        _control = new VerdictControl(_paused, _time);
        _drainer = DrainerFor(JudgeKind.ClaudeCode);
    }

    private VerdictDrainer DrainerFor(JudgeKind kind)
    {
        var judge = Options.Create(
            new JudgeOptions
            {
                Kind = kind,
                Environment = new() { ["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com" },
            }
        );

        return new VerdictDrainer(
            _store,
            null!,
            VerdictRunners.Create(
                _store,
                _set,
                _sources,
                _sessions,
                new NoWindows(),
                new PageLookups(new NoPages(), _set, _store),
                judge,
                new FakeTimeProvider(),
                NullLogger<VerdictRunner>.Instance
            ),
            new VerdictByRules(
                _store,
                _set,
                _sources,
                new PageLookups(new NoPages(), _set, _store),
                _time
            ),
            _sources,
            new VerdictSelector(_jobs, _set, judge: judge),
            _jobs,
            _control,
            new QuotaGuard(new UnknownQuota(), new FakeTimeProvider(), judge),
            _rejudges,
            _set,
            new EvidenceCompleter(_set, _sources, new FakeDetectors()),
            new DownloadRunner(_downloads),
            _paused,
            Options.Create(new CratesidePaths()),
            judge,
            _time
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
                    TrackKey = TrackKey,
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
        var folder = _set.Prepare(TrackKey);

        File.WriteAllText(_set.EvidencePathFor(TrackKey), evidence);

        if (withSource)
            File.WriteAllText(Path.Combine(folder, "source.flac"), "audio");
    }

    private Task<StageTick> JudgeBackfillAsync(VerdictDrainer? drainer = null) =>
        (drainer ?? _drainer).JudgeNextAsync(
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
        Assert.True(_jobs.TryGet(TrackKey, JobStage.Verdict, out var job));
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
                SourceUrl = $"https://music.youtube.com/watch?v={TrackKey}",
                Confidence = KeyConfidence.Medium,
                Center = new TonalCenter("A", DiatonicMode.Aeolian),
                Evidence = FullEvidence,
            }
        );
        _rejudges.Add("artist - song", DateTimeOffset.Parse("2026-10-02T00:00:00+00:00"), "test");

        await JudgeBackfillAsync();

        var download = Assert.Single(_downloads.Requests);

        Assert.Contains($"https://music.youtube.com/watch?v={TrackKey}", download.Arguments);
        Assert.Contains(_set.Prepare(TrackKey), download.Arguments);
        Assert.NotEmpty(_sessions.Prompts);
        Assert.DoesNotContain(JudgePrompt.NoAudioLine, _sessions.Prompts[0]);
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
                SourceUrl = $"https://music.youtube.com/watch?v={TrackKey}",
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

    [Fact]
    public async Task A_refused_session_holds_the_song_without_a_strike_and_starts_nothing_until_the_hold_passes()
    {
        Hold(FullEvidence, withSource: false);
        _sessions.Output =
            """{"type":"result","subtype":"success","is_error":true,"num_turns":1,"duration_ms":412,"api_error_status":429,"result":"API Error: Request rejected (429) · DeepSeek rate limit: Messages too frequent. Try again later."}""";

        var first = await JudgeBackfillAsync();

        Assert.Equal(StageOutcome.Held, first.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(1), first.RetryAfter);
        Assert.True(_jobs.TryGet(TrackKey, JobStage.Verdict, out var job));
        Assert.Equal(JobStatus.Held, job.Status);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromMinutes(1), _jobs.Updates()[^1].HeldUntil);
        Assert.Equal(0, job.Strikes);

        _time.Advance(TimeSpan.FromSeconds(50));

        var woken = await JudgeBackfillAsync();

        Assert.Equal(StageOutcome.Held, woken.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(10), woken.RetryAfter);
        Assert.Single(_sessions.Prompts);

        _time.Advance(TimeSpan.FromSeconds(10));

        var second = await JudgeBackfillAsync();

        Assert.Equal(2, _sessions.Prompts.Count);
        Assert.Equal(TimeSpan.FromMinutes(2), second.RetryAfter);
    }

    [Fact]
    public async Task Five_refusals_in_a_row_pause_judging_and_only_the_users_resume_tries_again()
    {
        Hold(FullEvidence, withSource: false);
        _sessions.Output =
            """{"type":"result","subtype":"success","is_error":true,"num_turns":1,"api_error_status":401,"result":"Failed to authenticate. API Error: 401 DeepSeek rejected every saved login. Log in and connect again."}""";

        foreach (var _ in Enumerable.Range(0, JudgeBackoff.PauseAfter))
        {
            Assert.False(_control.IsPaused);

            await JudgeBackfillAsync();
            _time.Advance(TimeSpan.FromMinutes(31));
        }

        Assert.True(_control.IsPausedByRefusals);
        Assert.Equal(JudgeBackoff.PauseAfter, _sessions.Prompts.Count);
        Assert.Contains("DeepSeek rejected every saved login", _control.Backoff.Last?.Reason);

        var stopped = await JudgeBackfillAsync();

        Assert.Equal(StageOutcome.Idle, stopped.Outcome);
        Assert.Equal(JudgeBackoff.PauseAfter, _sessions.Prompts.Count);
        Assert.True(_jobs.TryGet(TrackKey, JobStage.Verdict, out var job));
        Assert.Equal(0, job.Strikes);

        _control.Resume();

        await JudgeBackfillAsync();

        Assert.Equal(JudgeBackoff.PauseAfter + 1, _sessions.Prompts.Count);
    }

    [Fact]
    public async Task The_spend_down_does_not_try_a_refusing_endpoint_after_the_fifth_refusal()
    {
        Hold(FullEvidence, withSource: false);

        foreach (var _ in Enumerable.Range(0, JudgeBackoff.PauseAfter))
            _control.Backoff.Refused("no login", retryAfter: null);

        _time.Advance(TimeSpan.FromHours(2));

        var tick = await JudgeBackfillAsync();

        Assert.Equal(StageOutcome.Idle, tick.Outcome);
        Assert.Empty(_sessions.Prompts);
    }

    [Fact]
    public async Task A_session_the_endpoint_served_ends_the_backoff()
    {
        Hold(FullEvidence, withSource: false);
        _control.Backoff.Refused("rate limited", retryAfter: null);
        _control.Backoff.Refused("rate limited", retryAfter: null);
        _time.Advance(TimeSpan.FromMinutes(2));

        var tick = await JudgeBackfillAsync();

        Assert.Equal(StageOutcome.Ran, tick.Outcome);
        Assert.Null(_control.Backoff.Hold);
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            _control.Backoff.Refused("rate limited", retryAfter: null).RetryAt - _time.GetUtcNow()
        );
    }

    [Fact]
    public async Task A_track_the_rules_settle_is_recorded_as_a_ruling_and_no_session_runs()
    {
        Hold(AgreedEvidence, withSource: false);

        var tick = await JudgeBackfillAsync();

        Assert.True(tick.IsSuccess);
        Assert.Empty(_sessions.Prompts);

        var run = Assert.Single(_store.Read().Runs);

        Assert.Equal(DecidedBy.Rules, run.DecidedBy);
        Assert.Equal(KeyConfidence.Medium, run.Confidence);
        Assert.Equal("A minor", run.Key);
        Assert.Equal("artist - song", run.Song);
        Assert.True(_jobs.TryGet(TrackKey, JobStage.Verdict, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(
            "keydetect, madmom and Essentia name A minor; no Hooktheory or Musicnotes page states a key.",
            _store.EvidenceFor("artist - song")!.Reasoning
        );
    }

    [Fact]
    public async Task A_file_keyed_track_the_rules_settle_is_recorded_under_its_track_key()
    {
        const string fileKey = "file~25edb68200c05e82";
        Detected(fileKey, AgreedEvidence);

        var tick = await _drainer.RuleNextAsync(
            Intake((fileKey, "Dave Wise - Lockjaw's Saga")),
            TestContext.Current.CancellationToken
        );

        Assert.True(tick?.IsSuccess);

        var run = Assert.Single(_store.Read().Runs);

        Assert.Equal(DecidedBy.Rules, run.DecidedBy);
        Assert.Equal($"track:{fileKey}", run.SourceUrl);
        Assert.Equal(fileKey, run.SourceTrackKey);
    }

    private static WorkListsView Intake(params (string TrackKey, string Title)[] tracks) =>
        WorkListsView.Compose(
            [
                .. tracks.Select(track => new WorkItem
                {
                    TrackKey = track.TrackKey,
                    Title = track.Title,
                }),
            ],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction>()
        );

    private void Detected(string trackKey, string evidence)
    {
        _set.Prepare(trackKey);
        File.WriteAllText(_set.EvidencePathFor(trackKey), evidence);
    }

    [Fact]
    public async Task Paused_judging_still_lets_the_rules_rule_on_new_intake()
    {
        Detected("intake001", AgreedEvidence);
        _control.Pause();

        await _drainer.RuleNextAsync(
            Intake(("intake001", "Artist - Song")),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(DecidedBy.Rules, Assert.Single(_store.Read().Runs).DecidedBy);
        Assert.Empty(_sessions.Prompts);
    }

    [Fact]
    public async Task A_refusing_judge_endpoint_does_not_hold_the_rules()
    {
        Detected("intake001", AgreedEvidence);
        _control.Backoff.Refused("the judge's endpoint refused its key", TimeSpan.FromHours(1));

        await _drainer.RuleNextAsync(
            Intake(("intake001", "Artist - Song")),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(DecidedBy.Rules, Assert.Single(_store.Read().Runs).DecidedBy);
    }

    [Fact]
    public async Task A_track_the_rules_leave_open_does_not_keep_the_next_one_from_its_turn()
    {
        Detected("intake001", FullEvidence);
        Detected("intake002", AgreedEvidence);
        _control.Pause();

        await _drainer.RuleNextAsync(
            Intake(("intake001", "Artist - Open"), ("intake002", "Artist - Agreed")),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("artist - agreed", Assert.Single(_store.Read().Runs).Song);
        Assert.Empty(_sessions.Prompts);
    }

    [Fact]
    public async Task A_track_the_rules_leave_open_goes_to_the_judge_when_one_is_set_up()
    {
        Hold(FullEvidence, withSource: false);

        await JudgeBackfillAsync();

        Assert.NotEmpty(_sessions.Prompts);
    }

    [Fact]
    public async Task With_no_judge_a_track_the_rules_leave_open_waits_for_the_person_without_a_strike()
    {
        Hold(FullEvidence, withSource: false);

        var tick = await JudgeBackfillAsync(DrainerFor(JudgeKind.None));

        Assert.True(tick.IsSuccess);
        Assert.Empty(_sessions.Prompts);
        Assert.Empty(_store.Read().Runs);
        Assert.True(_jobs.TryGet(TrackKey, JobStage.Verdict, out var job));
        Assert.Equal(JobStatus.AwaitingPerson, job.Status);
        Assert.Equal(0, job.Strikes);
    }

    [Fact]
    public async Task With_no_judge_the_rules_still_rule()
    {
        Hold(AgreedEvidence, withSource: false);

        await JudgeBackfillAsync(DrainerFor(JudgeKind.None));

        Assert.Equal(DecidedBy.Rules, Assert.Single(_store.Read().Runs).DecidedBy);
    }

    [Fact]
    public async Task A_track_waiting_for_the_person_is_not_picked_again_while_there_is_no_judge()
    {
        Hold(FullEvidence, withSource: false);

        var none = DrainerFor(JudgeKind.None);

        await JudgeBackfillAsync(none);

        var again = await JudgeBackfillAsync(none);

        Assert.Equal(StageOutcome.Idle, again.Outcome);
    }

    [Fact]
    public async Task A_track_waiting_for_the_person_goes_to_the_judge_once_one_is_set_up()
    {
        Hold(FullEvidence, withSource: false);
        _jobs.Record(TrackKey, JobStage.Verdict, JobStatus.AwaitingPerson);

        await JudgeBackfillAsync();

        Assert.NotEmpty(_sessions.Prompts);
    }

    [Fact]
    public async Task With_no_judge_a_song_owed_a_re_judge_is_left_alone()
    {
        Hold(FullEvidence, withSource: true);
        _store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                Song = "artist - song",
                Artist = "Artist",
                Title = "Song",
                SourceUrl = $"https://music.youtube.com/watch?v={TrackKey}",
                Confidence = KeyConfidence.Medium,
                Center = new TonalCenter("A", DiatonicMode.Aeolian),
                Evidence = FullEvidence,
            }
        );
        _rejudges.Add("artist - song", DateTimeOffset.Parse("2026-10-02T00:00:00+00:00"), "test");

        var none = DrainerFor(JudgeKind.None);
        var tick = await JudgeBackfillAsync(none);

        Assert.Equal(StageOutcome.Idle, tick.Outcome);
        Assert.Empty(_sessions.Prompts);
        Assert.Equal(0, none.RejudgesWithEvidence(_store.Read()));
    }

    [Fact]
    public async Task With_no_judge_judging_from_bars_is_refused_without_touching_the_track()
    {
        var result = await DrainerFor(JudgeKind.None)
            .JudgeFromBarsAsync(
                new WorkOrder
                {
                    TrackKey = TrackKey,
                    Title = "Artist - Song",
                    SourceUrl = $"https://music.youtube.com/watch?v={TrackKey}",
                    Destination = StemDestination.UnresolvedKey,
                },
                new JudgeDirection([], null, null, null),
                _ => { },
                TestContext.Current.CancellationToken
            );

        Assert.False(result.IsSuccess);
        Assert.False(_jobs.TryGet(TrackKey, JobStage.Verdict, out _));
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

        /// <summary>What every session prints, as Claude Code's result JSON.</summary>
        public string Output { get; set; } = string.Empty;

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            EvidenceSeen.Add(readEvidence());
            Prompts.Add(request.StandardInput ?? string.Empty);

            return Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = 0,
                    StandardOutput = Output,
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
