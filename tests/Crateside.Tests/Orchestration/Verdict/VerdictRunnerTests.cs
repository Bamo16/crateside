using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Plans;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class VerdictRunnerTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";
    private const string SwappedTo = "-cuAnMUSN50";

    private const string Verdict = """
        {
          "artist": "Yes",
          "title": "Owner of a Lonely Heart",
          "confidence": "HIGH",
          "centre": { "tonic": "A", "mode": "aeolian" },
          "sources": [{ "url": "https://www.hooktheory.com/theorytab/view/yes/owner-of-a-lonely-heart", "kind": "authoritative", "statesKey": "A minor", "sections": ["Chorus"] }],
          "tonicCheck": "E7 resolves to Am.",
          "reasoning": "Backed by both, and Hooktheory agrees."
        }
        """;

    private const string AsksForWindows = """
        {
          "artist": "Yes",
          "title": "Owner of a Lonely Heart",
          "confidence": "WITHHELD",
          "reasoning": "A three-way split; the intro is drums only.",
          "windows": [{ "start": 62, "end": 94.5, "label": "Chorus" }]
        }
        """;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(
        DateTimeOffset.Parse("2026-10-16T12:00:00+00:00")
    );

    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));
    private LedgerStore Ledger => field ??= TestLedger.Store();
    private string VerdictPath => Path.Combine(Set.Prepare(VideoId), VerdictRunner.VerdictFileName);
    private FakeSessions Sessions { get; } = new();
    private FakeDetector Detector { get; } = new();

    private JudgeOptions Options { get; } =
        new() { Environment = new() { ["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com" } };

    public VerdictRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Ledger.Dispose();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private VerdictRunner Runner => new(Ledger, Set, Sessions, Detector, Options, _time);

    private static WorkOrder Order(bool instrumentalRelease = false, string? statedSource = null) =>
        WorkOrder.Compose(
            new WorkItem
            {
                PlaylistItemId = "PLI1",
                VideoId = VideoId,
                Title = "Yes - Owner of a Lonely Heart",
                Plan = instrumentalRelease ? StemPlan.InstrumentalRelease : StemPlan.Default,
                StatedSourceUrl = statedSource,
            },
            TestLedger.From("does-not-exist")
        );

    private void GiveEvidence(string evidence = """{"chords":["Am","E7"]}""")
    {
        Set.Prepare(VideoId);
        File.WriteAllText(Set.EvidencePathFor(VideoId), evidence);
    }

    private void GiveAudio() =>
        File.WriteAllText(Path.Combine(Set.Prepare(VideoId), "track.flac"), "audio");

    /// <summary>The next session writes this verdict file, or nothing, and says this.</summary>
    private void Session(
        string? writes,
        int exitCode = 0,
        string stdout = "",
        string stderr = ""
    ) =>
        Sessions.Turns.Enqueue(request =>
        {
            if (writes is not null)
                File.WriteAllText(VerdictPath, writes);

            return new ProcessOutcome
            {
                ExitCode = exitCode,
                StandardOutput = stdout,
                StandardError = stderr,
            };
        });

    private Task<VerdictResult> RunAsync(WorkOrder? order = null) =>
        Runner.RunAsync(order ?? Order(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_valid_verdict_file_is_recorded_under_the_orders_upload()
    {
        GiveEvidence();
        GiveAudio();
        Session(Verdict);

        var result = await RunAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(Ledger.Read().TryGetLatestByVideoId(VideoId, out var run));
        Assert.Equal("yes - owner of a lonely heart", run.Song);
        Assert.Equal(new TonalCentre("A", DiatonicMode.Aeolian), run.Centre);
        Assert.Equal(KeyConfidence.High, run.Confidence);

        var evidence = Ledger.EvidenceFor(run.Song)!;

        Assert.Equal(["Am", "E7"], evidence.Chords);
        Assert.Equal("full_mix", evidence.InputKind);
        Assert.Equal(["Chorus"], Assert.Single(evidence.Sources).Sections);
        Assert.Single(Sessions.Requests);
    }

    [Fact]
    public async Task A_session_that_writes_nothing_is_resumed_once_and_then_fails()
    {
        GiveEvidence();
        Session(writes: null);
        Session(writes: null);

        var result = await RunAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("no verdict file", result.Error);
        Assert.Empty(Ledger.Read().Runs);
        Assert.Equal(2, Sessions.Requests.Count);
        Assert.Equal(
            SessionId(Sessions.Requests[0], "--session-id"),
            SessionId(Sessions.Requests[1], "--resume")
        );
        Assert.StartsWith("Write the verdict file now", Sessions.Requests[1].StandardInput);
    }

    [Fact]
    public async Task A_refused_file_is_resumed_with_the_reason_and_recorded_once_fixed()
    {
        GiveEvidence();
        Session(Verdict.Replace("\"tonic\": \"A\"", "\"tonic\": \"H\""));
        Session(Verdict.Replace("\"tonic\": \"A\"", "\"tonic\": \"Bb\""));

        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("\"H\" is not a tonic", Sessions.Requests[1].StandardInput);
        Assert.True(Ledger.Read().TryGetLatestByVideoId(VideoId, out var run));
        Assert.Equal("A#", run.Centre!.Tonic);
    }

    [Fact]
    public async Task A_verdict_the_ledger_would_refuse_is_sent_back_rather_than_lost()
    {
        GiveEvidence();
        Session(
            Verdict.Replace(
                "\"centre\": { \"tonic\": \"A\", \"mode\": \"aeolian\" },",
                string.Empty
            )
        );
        Session(Verdict);

        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("centre", Sessions.Requests[1].StandardInput);
    }

    [Fact]
    public async Task Windows_asked_for_are_detected_and_the_same_session_resumed_with_their_evidence()
    {
        GiveEvidence();
        GiveAudio();
        Session(AsksForWindows);
        Session(Verdict);

        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal((62.0, 94.5), Assert.Single(Detector.Asked));
        Assert.Equal(
            SessionId(Sessions.Requests[0], "--session-id"),
            SessionId(Sessions.Requests[1], "--resume")
        );
        Assert.Contains("62–94.5 s (Chorus)", Sessions.Requests[1].StandardInput);
        Assert.Contains("window-evidence", Sessions.Requests[1].StandardInput);

        var evidence = Ledger.EvidenceFor("yes - owner of a lonely heart")!;

        Assert.Equal(
            new RunWindow(62, 94.5, "Chorus", WindowAsker.Judge),
            Assert.Single(evidence.Windows)
        );
        Assert.Equal(KeyConfidence.High, Assert.Single(Ledger.Read().Runs).Confidence);
    }

    [Fact]
    public async Task A_withheld_verdict_that_asked_for_windows_stands_when_the_resume_writes_nothing()
    {
        GiveEvidence();
        GiveAudio();
        Session(AsksForWindows);
        Session(writes: null);
        Session(writes: null);

        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(KeyConfidence.Withheld, Assert.Single(Ledger.Read().Runs).Confidence);
        Assert.Equal(3, Sessions.Requests.Count);
    }

    [Fact]
    public async Task Windows_are_asked_for_once_per_run()
    {
        GiveEvidence();
        GiveAudio();
        Session(AsksForWindows);
        Session(AsksForWindows);

        var result = await RunAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(Detector.Asked);
        Assert.Equal(2, Sessions.Requests.Count);
        Assert.Equal(KeyConfidence.Withheld, Assert.Single(Ledger.Read().Runs).Confidence);
    }

    [Fact]
    public async Task A_failed_session_that_wrote_nothing_says_what_it_said()
    {
        GiveEvidence();
        Session(writes: null, exitCode: 1, stderr: "connection reset");
        Session(writes: null, exitCode: 1, stderr: "quota exhausted");

        var result = await RunAsync();

        Assert.False(result.IsSuccess);
        Assert.False(result.IsAgentUnavailable);
        Assert.Contains("quota exhausted", result.Error);
    }

    [Fact]
    public async Task A_verdict_written_before_a_crash_still_counts()
    {
        GiveEvidence();
        Session(Verdict, exitCode: 1, stderr: "connection reset");

        Assert.True((await RunAsync()).IsSuccess);
    }

    [Fact]
    public async Task Running_out_of_allowance_is_not_the_tracks_fault()
    {
        // Reported as a successful run carrying an error, so the exit code says nothing.
        GiveEvidence();
        Session(
            writes: null,
            stdout: """{"is_error":true,"api_error_status":429,"result":"hit your limit"}"""
        );

        var result = await RunAsync();

        Assert.True(result.IsAgentUnavailable);
        Assert.Single(Sessions.Requests);
    }

    [Fact]
    public async Task Nobody_being_logged_in_is_not_the_tracks_fault_either()
    {
        GiveEvidence();
        Session(
            writes: null,
            stdout: """{"subtype":"success","is_error":true,"api_error_status":null,"result":"Not logged in - Please run /login"}"""
        );

        var result = await RunAsync();

        Assert.True(result.IsAgentUnavailable);
        Assert.Contains("/login", result.Error);
    }

    [Fact]
    public async Task A_source_swapped_before_its_verdict_is_linked_to_the_row()
    {
        GiveEvidence();
        Session(Verdict);

        var result = await RunAsync(
            Order(statedSource: $"https://music.youtube.com/watch?v={SwappedTo}")
        );

        Assert.True(result.IsSuccess);
        Assert.True(Ledger.Read().HasRunFor(SwappedTo));
        Assert.True(Ledger.Read().HasRunFor(VideoId));
    }

    [Fact]
    public async Task A_rejudged_song_keeps_its_identity_whatever_the_judge_calls_it()
    {
        GiveEvidence();
        Session(Verdict);
        await RunAsync();
        Session(Verdict.Replace("\"Yes\"", "\"YES (band)\""));

        await RunAsync();

        Assert.Contains("already judged as artist \"Yes\"", Sessions.Requests[1].StandardInput);
        Assert.Equal(1, Ledger.Read().SongCount);
    }

    [Fact]
    public async Task There_is_nothing_to_judge_without_evidence()
    {
        var result = await RunAsync();

        Assert.Contains("no evidence", result.Error);
        Assert.Empty(Sessions.Requests);
    }

    [Fact]
    public async Task The_session_has_the_four_tools_its_own_folder_and_the_configured_endpoint()
    {
        GiveEvidence();
        Session(Verdict);

        await RunAsync();

        var request = Assert.Single(Sessions.Requests);

        Assert.Equal("Read,Write,WebSearch,WebFetch", After(request, "--tools"));
        Assert.DoesNotContain(
            request.Arguments,
            argument => argument.Contains("Bash") || argument.Contains("Skill")
        );
        Assert.Equal(VerdictRunner.Instructions, After(request, "--append-system-prompt"));
        Assert.Equal(Set.Prepare(VideoId), request.WorkingDirectory);
        Assert.Equal("https://api.anthropic.com", request.Environment["ANTHROPIC_BASE_URL"]);
    }

    [Fact]
    public void The_work_order_names_the_evidence_and_the_verdict_file_and_carries_the_evidence()
    {
        GiveEvidence();

        var prompt = Runner.Prompt(Order(), VerdictPath);

        Assert.Contains(Set.EvidencePathFor(VideoId), prompt);
        Assert.Contains(VerdictPath, prompt);
        Assert.Contains("\"chords\":[\"Am\",\"E7\"]", prompt);
        Assert.Contains($"watch?v={VideoId}", prompt);
    }

    [Fact]
    public void Evidence_too_large_to_carry_is_pointed_at_instead()
    {
        GiveEvidence($$"""{"key_events":"{{new string('x', 64 * 1024)}}"}""");

        var prompt = Runner.Prompt(Order(), VerdictPath);

        Assert.DoesNotContain("```json", prompt);
        Assert.Contains(Set.EvidencePathFor(VideoId), prompt);
    }

    [Fact]
    public void The_stem_plan_is_never_mentioned_to_the_judge()
    {
        // Judging has nothing to do with which files get cut (ADR 0018).
        GiveEvidence();

        var prompt = Runner.Prompt(Order(instrumentalRelease: true), VerdictPath);

        Assert.DoesNotContain("instrumental", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_judge_is_told_how_much_the_ear_counts()
    {
        GiveEvidence();

        Assert.Contains("never raises confidence", Runner.Prompt(Order(), VerdictPath));

        Options.Ear = EarWeight.HumanSource;

        Assert.Contains("caps the verdict at MEDIUM", Runner.Prompt(Order(), VerdictPath));
    }

    [Fact]
    public void The_instructions_carry_the_two_fixed_rules()
    {
        Assert.Contains("caps the verdict at MEDIUM", VerdictRunner.Instructions);
        Assert.Contains("Tunebat", VerdictRunner.Instructions);
    }

    private static string After(ProcessRequest request, string flag) =>
        request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    private static string SessionId(ProcessRequest request, string flag) => After(request, flag);

    private sealed class FakeSessions : IProcessRunner
    {
        public Queue<Func<ProcessRequest, ProcessOutcome>> Turns { get; } = [];
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return Task.FromResult(Turns.Dequeue()(request));
        }
    }

    private sealed class FakeDetector : IWindowDetector
    {
        public List<(double, double)> Asked { get; } = [];

        public Task<EvidenceResult> DetectWindowAsync(
            string audioPath,
            RunWindow window,
            string evidencePath,
            CancellationToken ct = default
        )
        {
            Asked.Add((window.Start, window.End));
            File.WriteAllText(
                evidencePath,
                JsonSerializer.Serialize(new { note = "window-evidence" })
            );

            return Task.FromResult(EvidenceResult.Succeeded(evidencePath));
        }
    }
}
