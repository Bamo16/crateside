using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Driven against real child processes rather than mocks, for the same reason
/// <see cref="SeparationRunnerTests"/> is: the argument lists, the stdout plumbing and the
/// exit-code handling are the parts most likely to be wrong, and a fake exercises none of them.
/// </summary>
public class EvidenceRunnerTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string Working => Path.Combine(_directory, "working");
    private WorkingSet Set => field ??= new WorkingSet(Working);

    public EvidenceRunnerTests() => Directory.CreateDirectory(_directory);

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
            KeyLedger.Read("does-not-exist")
        );

    private string Script(string name, string body)
    {
        var path = Path.Combine(_directory, $"{name}.cmd");

        File.WriteAllText(path, $"@echo off{Environment.NewLine}{body}");

        return path;
    }

    /// <summary>A stand-in for stemforge-cli that prints an outcome and drops a file.</summary>
    private string Downloader(string stdout, int exitCode = 0, bool writesFile = true)
    {
        var audio = Path.Combine(Set.Prepare(VideoId), "Yes - Owner of a Lonely Heart.flac");

        return Script(
            "fake-stemforge",
            $"""
            {(writesFile ? $"""echo audio> "{audio}" """ : string.Empty)}
            echo {stdout}
            exit /b {exitCode}
            """
        );
    }

    /// <summary>A stand-in for uv that writes the machine layer where the runner asked for it.</summary>
    private string Detector(int exitCode = 0, bool writesEvidence = true) =>
        Script(
            "fake-uv",
            $"""
            {(
                writesEvidence
                    ? $"""echo machine-layer> "{Set.EvidencePathFor(VideoId)}" """
                    : string.Empty
            )}
            exit /b {exitCode}
            """
        );

    private EvidenceRunner Runner(string downloader, string detector) =>
        new(Set, new CratesidePaths(), downloader, detector);

    private static string DownloadedTo(string path) =>
        $$"""[{"succeeded":true,"path":"{{path.Replace(@"\", @"\\")}}"}]""";

    [Fact]
    public async Task A_finished_run_reports_where_the_evidence_landed()
    {
        var result = await Runner(Downloader(DownloadedTo(@"C:\working\track.flac")), Detector())
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Set.EvidencePathFor(VideoId), result.EvidencePath);
        Assert.True(Set.HasEvidence(VideoId));
    }

    [Fact]
    public async Task Evidence_already_on_disk_is_not_produced_again()
    {
        // Re-detecting burns 15-40s of CPU to reach the answer already sitting there. Both
        // stand-ins fail, so anything that ran at all would fail the run.
        Directory.CreateDirectory(Set.Prepare(VideoId));
        File.WriteAllText(Set.EvidencePathFor(VideoId), "{}");

        var result = await Runner(
                Downloader("boom", exitCode: 1, writesFile: false),
                Detector(exitCode: 1, writesEvidence: false)
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_retained_download_is_reused_rather_than_fetched_again()
    {
        // The whole pipeline costs one fetch per track. The downloader here fails, so a run
        // that succeeds is one that never called it.
        File.WriteAllText(Path.Combine(Set.Prepare(VideoId), "already-here.flac"), "audio");

        var result = await Runner(Script("fake-stemforge", "exit /b 1"), Detector())
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_failed_download_says_what_the_downloader_said()
    {
        var result = await Runner(
                Downloader("no such video", exitCode: 1, writesFile: false),
                Detector()
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("no such video", result.Error);
    }

    [Fact]
    public async Task A_per_input_failure_is_read_out_of_the_json_despite_a_clean_exit()
    {
        // The CLI reports per-input failures inside its JSON rather than by failing the whole
        // process, so a zero exit is not on its own evidence that anything worked.
        var result = await Runner(
                Downloader(
                    """[{"succeeded":false,"error":"format unavailable"}]""",
                    writesFile: false
                ),
                Detector()
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("format unavailable", result.Error);
    }

    [Fact]
    public async Task Unreadable_download_output_is_a_failure_rather_than_a_guess()
    {
        var result = await Runner(Downloader("something went sideways"), Detector())
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("no readable JSON", result.Error);
    }

    [Fact]
    public async Task A_detector_that_exits_clean_without_writing_anything_has_not_produced_evidence()
    {
        // Checking reality rather than the exit code, the same idiom the verdict stage uses:
        // the artifact is the point, and the next stage reads the file rather than the claim.
        var result = await Runner(
                Downloader(DownloadedTo(@"C:\working\track.flac")),
                Detector(writesEvidence: false)
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("wrote no evidence", result.Error);
    }

    [Fact]
    public async Task A_failed_detector_says_what_it_said()
    {
        var result = await Runner(
                Downloader(DownloadedTo(@"C:\working\track.flac")),
                Script("fake-uv", "echo ffmpeg not found 1>&2" + Environment.NewLine + "exit /b 2")
            )
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("ffmpeg not found", result.Error);
    }
}
