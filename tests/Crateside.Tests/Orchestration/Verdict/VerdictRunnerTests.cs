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
    private FakePages Pages { get; } = new();

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

    private VerdictRunner Runner => new(Ledger, Set, Sessions, Detector, Pages, Options, _time);

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
        Assert.Contains("\"Chorus\", 62–94.5 s", Sessions.Requests[1].StandardInput);
        Assert.Contains("window-evidence", Sessions.Requests[1].StandardInput);

        var evidence = Ledger.EvidenceFor("yes - owner of a lonely heart")!;

        Assert.Equal(
            new RunWindow(62, 94.5, "Chorus", WindowAsker.Judge)
            {
                Why = "A three-way split; the intro is drums only.",
            },
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

        var prompt = Runner.Prompt(Order(), VerdictPath, []);

        Assert.Contains(Set.EvidencePathFor(VideoId), prompt);
        Assert.Contains(VerdictPath, prompt);
        Assert.Contains("\"chords\":[\"Am\",\"E7\"]", prompt);
        Assert.Contains($"watch?v={VideoId}", prompt);
    }

    [Fact]
    public void Evidence_too_large_to_carry_is_pointed_at_instead()
    {
        GiveEvidence($$"""{"key_events":"{{new string('x', 64 * 1024)}}"}""");

        var prompt = Runner.Prompt(Order(), VerdictPath, []);

        Assert.DoesNotContain("```json", prompt);
        Assert.Contains(Set.EvidencePathFor(VideoId), prompt);
    }

    [Fact]
    public void The_stem_plan_is_never_mentioned_to_the_judge()
    {
        // Judging has nothing to do with which files get cut (ADR 0018).
        GiveEvidence();

        var prompt = Runner.Prompt(Order(instrumentalRelease: true), VerdictPath, []);

        Assert.DoesNotContain("instrumental", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_judge_is_told_how_much_the_ear_counts()
    {
        GiveEvidence();

        Assert.Contains("never raises confidence", Runner.Prompt(Order(), VerdictPath, []));

        Options.Ear = EarWeight.HumanSource;

        Assert.Contains("caps the verdict at MEDIUM", Runner.Prompt(Order(), VerdictPath, []));
    }

    [Fact]
    public void The_instructions_carry_the_two_fixed_rules()
    {
        Assert.Contains("caps the verdict at MEDIUM", VerdictRunner.Instructions);
        Assert.Contains("Tunebat", VerdictRunner.Instructions);
    }

    [Fact]
    public async Task Pages_are_looked_up_by_the_rows_artist_and_title_and_reach_the_work_order()
    {
        GiveEvidence();
        Session(Verdict);

        await RunAsync();

        Assert.Equal(("Yes", "Owner of a Lonely Heart"), Assert.Single(Pages.Asked));
        Assert.Contains(
            "- Hooktheory, https://www.hooktheory.com/theorytab/view/yes/owner-of-a-lonely-heart: "
                + "\"Owner of a Lonely Heart by Yes\". Chorus: A minor, 124 BPM",
            Sessions.Requests[0].StandardInput
        );
        Assert.Contains("- Musicnotes: nothing found", Sessions.Requests[0].StandardInput);
        Assert.Contains(
            "- Ultimate Guitar: not reached (timed out)",
            Sessions.Requests[0].StandardInput
        );
    }

    [Fact]
    public async Task Pages_are_fetched_once_per_track_and_then_read_from_beside_the_evidence()
    {
        GiveEvidence();
        Session(writes: null);
        Session(writes: null);
        await RunAsync();
        Session(Verdict);

        await RunAsync();

        Assert.Single(Pages.Asked);
        Assert.Contains("Chorus: A minor", Sessions.Requests[2].StandardInput);
    }

    [Fact]
    public async Task A_row_with_no_artist_in_its_title_is_not_looked_up()
    {
        GiveEvidence();
        Session(Verdict);

        await RunAsync(
            WorkOrder.Compose(
                new WorkItem
                {
                    PlaylistItemId = "PLI1",
                    VideoId = VideoId,
                    Title = "Owner of a Lonely Heart",
                },
                TestLedger.From("does-not-exist")
            )
        );

        Assert.Empty(Pages.Asked);
    }

    [Fact]
    public async Task A_bracketed_tail_is_left_out_of_the_lookup()
    {
        GiveEvidence();
        Session(Verdict);

        await RunAsync(
            WorkOrder.Compose(
                new WorkItem
                {
                    PlaylistItemId = "PLI1",
                    VideoId = VideoId,
                    Title = "Yes - Owner of a Lonely Heart (2003 Remaster) [Official Video]",
                },
                TestLedger.From("does-not-exist")
            )
        );

        Assert.Equal(("Yes", "Owner of a Lonely Heart"), Assert.Single(Pages.Asked));
    }

    private static string After(ProcessRequest request, string flag) =>
        request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    private static string SessionId(ProcessRequest request, string flag) => After(request, flag);

    private const string FromTheBars = """
        {
          "artist": "Yes",
          "title": "Owner of a Lonely Heart",
          "confidence": "HIGH",
          "centre": { "tonic": "C#", "mode": "aeolian" },
          "sections": [
            { "label": "Chorus", "tonic": "C#", "mode": "aeolian" },
            { "label": "bars 82–96", "tonic": "C#", "mode": "aeolian" }
          ],
          "sources": [{ "kind": "ear", "states": "C# minor" }],
          "reasoning": "Both windows read C# minor."
        }
        """;

    private string AudioPath => Path.Combine(_directory, "kept source.flac");

    private JudgeDirection Direction(TonalCentre? ear = null) =>
        new(
            [
                new RunWindow(45, 67, "Chorus", WindowAsker.User) { Bars = new(33, 48) },
                new RunWindow(112, 133, null, WindowAsker.User) { Bars = new(82, 96) },
            ],
            "The air horn throws the detectors; listen to the choruses.",
            ear,
            AudioPath
        );

    private Task<VerdictResult> JudgeFromBarsAsync(
        JudgeDirection direction,
        List<JudgeStep> steps
    ) => Runner.RunAsync(Order(), direction, steps.Add, TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_users_windows_are_detected_before_the_first_session_and_sent_with_it()
    {
        GiveEvidence();
        Session(FromTheBars);
        List<JudgeStep> steps = [];

        var result = await JudgeFromBarsAsync(Direction(), steps);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([(45.0, 67.0), (112.0, 133.0)], Detector.Asked);

        var prompt = Assert.Single(Sessions.Requests).StandardInput!;

        Assert.Contains("\"Chorus\", 45–67 s", prompt);
        Assert.Contains("\"bars 82–96\", 112–133 s", prompt);
        Assert.Contains("C# minor @ 112", prompt);
        Assert.Contains("listen to the choruses", prompt);
        Assert.Equal(
            [
                new JudgeStep.DetectingWindow(1, 2, Direction().Windows[0]),
                new JudgeStep.DetectingWindow(2, 2, Direction().Windows[1]),
                new JudgeStep.Judging(),
            ],
            steps
        );
    }

    [Fact]
    public async Task A_run_from_bars_records_the_windows_as_sections_with_the_note()
    {
        GiveEvidence();
        Session(FromTheBars);

        await JudgeFromBarsAsync(Direction(), []);

        var evidence = Ledger.EvidenceFor("yes - owner of a lonely heart")!;

        Assert.Equal(Direction().Windows, evidence.Windows);
        Assert.Equal(
            "The air horn throws the detectors; listen to the choruses.",
            evidence.Direction
        );
        Assert.Equal(
            [("Chorus", "C# minor @ 45"), ("bars 82–96", "C# minor @ 112")],
            evidence.Readings.Select(reading =>
                (reading.Section!.Label, reading.Algorithms.Single(a => a.Key is not null).Key)
            )
        );
        Assert.Equal("full_mix", evidence.InputKind);
    }

    [Fact]
    public async Task What_the_user_hears_is_recorded_by_crateside_once_with_how_much_it_counted()
    {
        GiveEvidence();
        Options.Ear = EarWeight.HumanSource;
        Session(FromTheBars);

        await JudgeFromBarsAsync(Direction(new TonalCentre("C#", DiatonicMode.Aeolian)), []);

        var ear = Assert.Single(Ledger.EvidenceFor("yes - owner of a lonely heart")!.Sources);

        Assert.Equal(SourceKind.Ear, ear.Kind);
        Assert.Equal("C# minor", ear.StatesKey);
        Assert.Equal("counted as a human source", ear.Note);
        Assert.Contains("counts like an authoritative source", Sessions.Requests[0].StandardInput);
        Assert.Contains("What they hear: C# minor", Sessions.Requests[0].StandardInput);
    }

    [Fact]
    public async Task Without_evidence_in_the_working_set_the_whole_track_is_detected_first()
    {
        Session(FromTheBars);
        List<JudgeStep> steps = [];

        var result = await JudgeFromBarsAsync(Direction(), steps);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, Detector.WholeAsked);
        Assert.IsType<JudgeStep.DetectingWhole>(steps[0]);
        Assert.Equal(["C#m", "G#"], Ledger.EvidenceFor("yes - owner of a lonely heart")!.Chords);
    }

    [Fact]
    public async Task Windows_the_judge_asks_for_in_a_run_from_bars_follow_the_users_with_its_reason()
    {
        GiveEvidence();
        Session(AsksForWindows);
        Session(FromTheBars);
        List<JudgeStep> steps = [];

        await JudgeFromBarsAsync(Direction(), steps);

        Assert.Equal([(45.0, 67.0), (112.0, 133.0), (62.0, 94.5)], Detector.Asked);
        Assert.True(File.Exists(Path.Combine(Set.Prepare(VideoId), "evidence.window3.json")));
        Assert.Contains(steps, step => step is JudgeStep.JudgeAsked);

        var windows = Ledger.EvidenceFor("yes - owner of a lonely heart")!.Windows;

        Assert.Equal(
            [WindowAsker.User, WindowAsker.User, WindowAsker.Judge],
            windows.Select(window => window.AskedBy)
        );
        Assert.Equal("A three-way split; the intro is drums only.", windows[2].Why);
    }

    [Fact]
    public async Task A_run_from_bars_that_writes_nothing_is_resumed_once_and_says_so()
    {
        GiveEvidence();
        Session(writes: null);
        Session(FromTheBars);
        List<JudgeStep> steps = [];

        var result = await JudgeFromBarsAsync(Direction(), steps);

        Assert.True(result.IsSuccess, result.Error);
        Assert.IsType<JudgeStep.Resuming>(steps[^1]);
        Assert.Equal(2, Sessions.Requests.Count);
    }

    [Fact]
    public async Task A_run_from_bars_with_no_ranges_judges_the_whole_track_again()
    {
        GiveEvidence();
        Session(Verdict);

        var result = await JudgeFromBarsAsync(new JudgeDirection([], null, null, AudioPath), []);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Detector.Asked);
        Assert.Contains("judged again, over the whole track", Sessions.Requests[0].StandardInput);
        Assert.Empty(Ledger.EvidenceFor("yes - owner of a lonely heart")!.Windows);
    }

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

    private sealed class FakePages : ISourcePages
    {
        public List<(string, string)> Asked { get; } = [];

        public Task<IReadOnlyList<SiteLookup>> FetchAsync(
            string artist,
            string title,
            CancellationToken ct = default
        )
        {
            Asked.Add((artist, title));

            return Task.FromResult<IReadOnlyList<SiteLookup>>([
                new(
                    "Hooktheory",
                    LookupStatus.Found,
                    [
                        new FetchedPage
                        {
                            Site = "Hooktheory",
                            Url =
                                "https://www.hooktheory.com/theorytab/view/yes/owner-of-a-lonely-heart",
                            Heading = "Owner of a Lonely Heart by Yes",
                            Keys = [new PageKey("A minor", "Chorus", 124)],
                        },
                    ]
                ),
                new("Musicnotes", LookupStatus.NotFound, []),
                new("Ultimate Guitar", LookupStatus.Unreachable, []) { Error = "timed out" },
            ]);
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
                JsonSerializer.Serialize(
                    new
                    {
                        note = "window-evidence",
                        algorithms = new
                        {
                            ffmpeg_keydetect = new { key = $"C# minor @ {window.Start}" },
                        },
                    }
                )
            );

            return Task.FromResult(EvidenceResult.Succeeded(evidencePath));
        }

        public int WholeAsked { get; private set; }

        public Task<EvidenceResult> DetectWholeAsync(
            string audioPath,
            string evidencePath,
            CancellationToken ct = default
        )
        {
            WholeAsked++;
            File.WriteAllText(evidencePath, """{"chords":["C#m","G#"]}""");

            return Task.FromResult(EvidenceResult.Succeeded(evidencePath));
        }
    }
}
