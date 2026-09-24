using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>Driven against a real child process rather than a mocked one.</summary>
public class SeparationRunnerTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeProcessProbe _probe = new();

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private SeparationInterlock Interlock => field ??= new SeparationInterlock(Jobs, _probe);
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    /// <summary>Never the real paths: the runner writes to these.</summary>
    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            StemsKeyIdentified = Path.Combine(_directory, "Key Identified"),
            StemsUnresolvedKey = Path.Combine(_directory, "Unresolved Key"),
        };

    public SeparationRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static WorkOrder InstrumentalOrder() =>
        WorkOrder.Compose(
            new QueueItem
            {
                PlaylistItemId = "PLI1",
                VideoId = VideoId,
                Title = "Yes - Owner of a Lonely Heart",
                Plan = StemPlan.InstrumentalRelease,
            },
            Crateside.Core.Ledger.KeyLedger.Read("does-not-exist")
        );

    private static WorkOrder Order() =>
        WorkOrder.Compose(
            new QueueItem
            {
                PlaylistItemId = "PLI1",
                VideoId = VideoId,
                Title = "Yes - Owner of a Lonely Heart",
            },
            Crateside.Core.Ledger.KeyLedger.Read("does-not-exist")
        );

    /// <summary>A stand-in for stemforge-cli that prints what the test wants and exits.</summary>
    private SeparationRunner RunnerPrinting(string stdout, int exitCode = 0)
    {
        var script = Path.Combine(_directory, "fake-stemforge.cmd");

        File.WriteAllText(
            script,
            $"""
            @echo off
            echo {stdout}
            exit /b {exitCode}
            """
        );

        return new SeparationRunner(Interlock, Set, Paths, script);
    }

    [Fact]
    public async Task A_finished_run_reports_the_stems_it_wrote()
    {
        var runner = RunnerPrinting(
            """[{"succeeded":true,"outputFiles":["C:\\Stems\\vocal.flac","C:\\Stems\\instrumental.flac","C:\\Stems\\drums.flac"]}]"""
        );

        var result = await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        // A full Standard recipe's worth; fewer than the recipe asked for is a failure.
        Assert.Equal(SeparationOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.OutputFiles.Count);
        Assert.Contains(@"C:\Stems\vocal.flac", result.OutputFiles);
    }

    [Fact]
    public async Task A_finished_run_frees_the_gpu()
    {
        await RunnerPrinting("""[{"succeeded":true,"outputFiles":["v.flac","i.flac","d.flac"]}]""")
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(Jobs.TryGet(VideoId, JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(0, job.Strikes);
    }

    [Fact]
    public async Task A_per_input_failure_is_caught_even_though_the_process_exited_cleanly()
    {
        // The CLI reports a track's failure inside its JSON rather than by failing the run, so
        // trusting the exit code alone would record a success that produced no stems.
        var runner = RunnerPrinting("""[{"succeeded":false,"error":"cuda out of memory"}]""");

        var result = await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Failed, result.Outcome);
        Assert.Equal("cuda out of memory", result.Error);

        Assert.True(Jobs.TryGet(VideoId, JobStage.Separation, out var job));
        Assert.Equal(1, job.Strikes);
    }

    [Fact]
    public async Task A_non_zero_exit_fails_the_run()
    {
        var result = await RunnerPrinting("something went wrong", exitCode: 3)
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Success_with_no_readable_json_is_not_treated_as_success()
    {
        var result = await RunnerPrinting("Separating...")
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Failed, result.Outcome);
        Assert.Contains("JSON", result.Error);
    }

    [Fact]
    public async Task A_second_separation_is_refused_while_one_is_live()
    {
        Interlock.Claim("otherVideo0", processId: 4242, DateTimeOffset.UnixEpoch);
        _probe.Running[4242] = DateTimeOffset.UnixEpoch;

        var result = await RunnerPrinting("""[{"succeeded":true}]""")
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Refused, result.Outcome);
        Assert.Contains("otherVideo0", result.Error);

        Assert.False(Jobs.TryGet(VideoId, JobStage.Separation, out _));
    }

    [Fact]
    public async Task Cancelling_stops_the_process_and_frees_the_gpu()
    {
        var script = Path.Combine(_directory, "slow-stemforge.cmd");

        // Long enough that the run is still going when the token trips.
        File.WriteAllText(
            script,
            """
            @echo off
            ping -n 30 127.0.0.1 > nul
            echo [{"succeeded":true}]
            """
        );

        var runner = new SeparationRunner(Interlock, Set, Paths, script);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(Order(), cancellation.Token)
        );

        Assert.True(Jobs.TryGet(VideoId, JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("cancelled", job.Error);
    }

    /// <summary>A stand-in that writes its own argument list out for inspection.</summary>
    private SeparationRunner RunnerEchoingArguments(out string argumentsFile)
    {
        var script = Path.Combine(_directory, "echo-stemforge.cmd");

        argumentsFile = Path.Combine(_directory, "arguments.txt");

        File.WriteAllText(
            script,
            $$"""
            @echo off
            echo %* > "{{argumentsFile}}"
            echo [{"succeeded":true,"outputFiles":["one.flac","two.flac","three.flac"]}]
            """
        );

        return new SeparationRunner(Interlock, Set, Paths, script);
    }

    [Fact]
    public async Task The_retained_download_is_separated_rather_than_fetched_again()
    {
        var audio = Path.Combine(Set.Prepare(VideoId), "Yes - Owner of a Lonely Heart.flac");

        File.WriteAllText(audio, "audio");

        var runner = RunnerEchoingArguments(out var argumentsFile);

        await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        var arguments = File.ReadAllText(argumentsFile);

        Assert.Contains(audio, arguments);
        Assert.DoesNotContain("youtube.com", arguments);
    }

    [Fact]
    public async Task A_track_with_nothing_retained_is_still_fetched_by_url()
    {
        var runner = RunnerEchoingArguments(out var argumentsFile);

        await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Contains($"watch?v={VideoId}", File.ReadAllText(argumentsFile));
    }

    [Fact]
    public async Task A_kept_source_is_no_longer_this_stages_business()
    {
        // The download stage copies it (ADR 0019).
        var audio = Path.Combine(Set.Prepare(VideoId), "Yes - Owner of a Lonely Heart.flac");

        File.WriteAllText(audio, "the source");

        var runner = RunnerEchoingArguments(out var argumentsFile);

        var result = await runner.RunAsync(
            InstrumentalOrder(),
            TestContext.Current.CancellationToken
        );

        var kept = Path.Combine(
            Paths.DestinationFor(StemDestination.UnresolvedKey),
            "Yes - Owner of a Lonely Heart.flac"
        );

        Assert.False(File.Exists(kept), "separation copied a source it no longer owns");
        Assert.DoesNotContain(kept, result.OutputFiles);
        Assert.DoesNotContain("--keep-source", File.ReadAllText(argumentsFile));
    }

    [Fact]
    public async Task A_run_that_made_less_than_the_recipe_asked_for_has_not_succeeded()
    {
        var runner = RunnerPrinting(
            """[{"succeeded":true,"outputFiles":["C:\\Stems\\Burst Mode (Vocal - Full).flac"]}]"""
        );

        // The usual pair plus a guide: three asked for, one produced.
        var result = await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Failed, result.Outcome);
        Assert.Contains("3 files", result.Error);
        Assert.Contains("produced 1", result.Error);

        Assert.True(Jobs.TryGet(VideoId, JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public async Task More_files_than_asked_for_is_not_a_failure()
    {
        var runner = RunnerPrinting(
            """[{"succeeded":true,"outputFiles":["a.flac","b.flac","c.flac","d.flac"]}]"""
        );

        var result = await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public void The_expected_count_follows_the_recipe_rather_than_any_name()
    {
        // Counted, not name-matched: the names are StemForge's.
        Assert.Equal(3, SeparationRecipe.Standard.ExpectedOutputs);
        // The kept source is not among them: the download stage copies it (ADR 0019).
        Assert.Equal(1, SeparationRecipe.InstrumentalRelease.ExpectedOutputs);
    }

    [Fact]
    public async Task An_ordinary_release_keeps_no_source_either_way()
    {
        File.WriteAllText(Path.Combine(Set.Prepare(VideoId), "track.flac"), "the source");

        var runner = RunnerEchoingArguments(out var argumentsFile);

        var result = await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("--keep-source", File.ReadAllText(argumentsFile));
        Assert.DoesNotContain(
            result.OutputFiles,
            f => f.EndsWith("track.flac", StringComparison.Ordinal)
        );
    }
}
