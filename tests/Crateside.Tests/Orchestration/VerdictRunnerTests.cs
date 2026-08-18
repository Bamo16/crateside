using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

public class VerdictRunnerTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string LedgerPath => Path.Combine(_directory, "runs.jsonl");
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));
    private CratesidePaths Paths => field ??= new CratesidePaths { Ledger = LedgerPath };

    public VerdictRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static WorkOrder Order(bool instrumentalRelease = false) =>
        WorkOrder.Compose(
            new QueueItem
            {
                PlaylistItemId = "PLI1",
                VideoId = VideoId,
                Title = "Yes - Owner of a Lonely Heart",
                IsInstrumentalRelease = instrumentalRelease,
            },
            KeyLedger.Read("does-not-exist")
        );

    private void GiveEvidence()
    {
        Set.Prepare(VideoId);
        File.WriteAllText(Set.EvidencePathFor(VideoId), "{}");
    }

    /// <summary>Writes the ledger line a real session would have recorded.</summary>
    private void RecordVerdict() =>
        File.WriteAllText(
            LedgerPath,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-17T00:00:00+00:00",
                    song = "yes - owner of a lonely heart",
                    artist = "Yes",
                    title = "Owner of a Lonely Heart",
                    source_url = $"https://music.youtube.com/watch?v={VideoId}",
                    verdict = new { key = "A minor", confidence = "HIGH" },
                }
            )
        );

    /// <summary>A stand-in for the agent that records a verdict, or does not, and exits.</summary>
    private VerdictRunner Runner(
        bool records,
        int exitCode = 0,
        string stderr = "",
        string stdout = ""
    )
    {
        var script = Path.Combine(_directory, "fake-claude.cmd");

        if (records)
            RecordVerdict();

        File.WriteAllText(
            script,
            $"""
            @echo off
            {(stdout is { Length: > 0 } ? $"echo {stdout}" : string.Empty)}
            {(stderr is { Length: > 0 } ? $"echo {stderr} 1>&2" : string.Empty)}
            exit /b {exitCode}
            """
        );

        return new VerdictRunner(Set, Paths, new JudgingSession(), script);
    }

    [Fact]
    public async Task A_recorded_line_is_what_success_means()
    {
        GiveEvidence();

        var result = await Runner(records: true)
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_session_that_exits_clean_without_recording_has_not_produced_a_verdict()
    {
        // Reality, not a claim. The artifact is the point, and it is the exact negation of the
        // selector's own predicate -- so a session that died halfway is simply picked up again
        // with no special handling.
        GiveEvidence();

        var result = await Runner(records: false)
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("without recording a verdict", result.Error);
    }

    [Fact]
    public async Task A_verdict_recorded_before_a_crash_still_counts()
    {
        // The exit code is kept for the message and the strike count and decides nothing. A
        // session that recorded the line and then fell over has done the work.
        GiveEvidence();

        var result = await Runner(records: true, exitCode: 1, stderr: "connection reset")
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_failed_session_that_recorded_nothing_says_what_it_said()
    {
        GiveEvidence();

        var result = await Runner(records: false, exitCode: 1, stderr: "quota exhausted")
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("quota exhausted", result.Error);
    }

    [Fact]
    public async Task There_is_nothing_to_judge_without_evidence()
    {
        // Spending an agent session to discover there is no input would be the expensive way to
        // find out, on the one stage that costs quota.
        var result = await Runner(records: true)
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("no evidence", result.Error);
    }

    [Fact]
    public async Task Running_out_of_allowance_is_not_the_tracks_fault()
    {
        // The session reports this as a successful run that carries an error, so the exit code
        // says nothing. Left as an ordinary failure it burned all three strikes in about a
        // second -- the retries returned instantly, cost nothing, and killed the track for good,
        // so that when the allowance came back it was already out.
        GiveEvidence();

        var result = await Runner(
                records: false,
                stdout: """{"is_error":true,"api_error_status":429,"result":"hit your limit"}"""
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsQuotaExhausted);
    }

    [Fact]
    public async Task An_ordinary_failure_is_still_the_tracks_own()
    {
        GiveEvidence();

        var result = await Runner(records: false, exitCode: 1, stderr: "something broke")
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsQuotaExhausted);
    }

    [Fact]
    public void The_prompt_names_the_evidence_and_forbids_acquiring_audio()
    {
        GiveEvidence();

        var prompt = Runner(records: false).Prompt(Order());

        Assert.StartsWith("/detect-key", prompt);
        Assert.Contains(Set.EvidencePathFor(VideoId), prompt);
        Assert.Contains("Do not acquire audio", prompt);
        Assert.Contains($"watch?v={VideoId}", prompt);
    }

    [Fact]
    public void The_retained_audio_is_offered_only_for_a_segment_rerun()
    {
        GiveEvidence();
        File.WriteAllText(Path.Combine(Set.Prepare(VideoId), "track.flac"), "audio");

        var prompt = Runner(records: false).Prompt(Order());

        Assert.Contains("read-only", prompt);
        Assert.Contains("--segment", prompt);
    }

    [Fact]
    public void Audio_that_is_not_there_is_not_promised()
    {
        GiveEvidence();

        Assert.DoesNotContain("--segment", Runner(records: false).Prompt(Order()));
    }

    [Fact]
    public void An_instrumental_release_is_passed_through_rather_than_left_to_be_inferred()
    {
        // The user stated it in the Queue view, which is the only place in the pipeline where a
        // user is present. The session must record it, never re-derive it from a title.
        GiveEvidence();

        var prompt = Runner(records: false).Prompt(Order(instrumentalRelease: true));

        Assert.Contains("has_vocals: false", prompt);
        Assert.Contains("do not re-derive", prompt);
    }

    [Fact]
    public void Nothing_is_said_about_vocals_when_nobody_said_anything()
    {
        GiveEvidence();

        Assert.DoesNotContain("has_vocals", Runner(records: false).Prompt(Order()));
    }
}
