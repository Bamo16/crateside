using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Driven against a real child process rather than a mocked one: the argument list, the stdout
/// plumbing and the exit-code handling are the parts most likely to be wrong, and none of them
/// are exercised by a fake.
/// </summary>
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

    public SeparationRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

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

        return new SeparationRunner(Interlock, new CratesidePaths(), script);
    }

    [Fact]
    public async Task A_finished_run_reports_the_stems_it_wrote()
    {
        var runner = RunnerPrinting(
            """[{"succeeded":true,"outputFiles":["C:\\Stems\\vocal.flac","C:\\Stems\\drums.flac"]}]"""
        );

        var result = await runner.RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.OutputFiles.Count);
        Assert.Contains(@"C:\Stems\vocal.flac", result.OutputFiles);
    }

    [Fact]
    public async Task A_finished_run_frees_the_gpu()
    {
        await RunnerPrinting("""[{"succeeded":true,"outputFiles":[]}]""")
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
        // Better to record a failure and retry than to mark a track done on no evidence.
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

        // Never two at once -- this is the one condition the runner will not work around.
        Assert.Equal(SeparationOutcome.Refused, result.Outcome);
        Assert.Contains("otherVideo0", result.Error);

        // And being refused is not the track's fault, so it earns no strike and no record.
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

        var runner = new SeparationRunner(Interlock, new CratesidePaths(), script);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(Order(), cancellation.Token)
        );

        // Pausing must not leave the GPU held by a process nobody is watching.
        Assert.True(Jobs.TryGet(VideoId, JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("cancelled", job.Error);
    }
}
