using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Orchestration.Evidence;

/// <summary>Running the detectors over audio crateside already holds (ADR 0019).</summary>
public sealed class EvidenceRunnerTests : IDisposable
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

    private void Held()
    {
        File.WriteAllText(
            Path.Combine(Set.Prepare(VideoId), "Yes - Owner of a Lonely Heart.flac"),
            "audio"
        );
    }

    /// <summary>A stand-in for uv that writes the machine layer where the runner asked for it.</summary>
    private string Detector(int exitCode = 0, bool writesEvidence = true)
    {
        var path = Path.Combine(_directory, "fake-uv.cmd");

        File.WriteAllText(
            path,
            $"""
            @echo off
            {(
                writesEvidence
                    ? $"""echo machine-layer> "{Set.EvidencePathFor(VideoId)}" """
                    : string.Empty
            )}
            exit /b {exitCode}
            """
        );

        return path;
    }

    private EvidenceRunner Runner(string detector) => new(Set, new CratesidePaths(), detector);

    [Fact]
    public async Task A_finished_run_reports_where_the_evidence_landed()
    {
        Held();

        var result = await Runner(Detector())
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Set.EvidencePathFor(VideoId), result.EvidencePath);
        Assert.True(Set.HasEvidence(VideoId));
    }

    [Fact]
    public async Task Evidence_already_on_disk_is_not_produced_again()
    {
        // The stand-in fails, so anything that ran at all would fail the run.
        Held();
        File.WriteAllText(Set.EvidencePathFor(VideoId), "{}");

        var result = await Runner(Detector(exitCode: 1, writesEvidence: false))
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_track_with_no_audio_held_for_it_is_not_this_stages_to_run()
    {
        var result = await Runner(Detector())
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("working set", result.Error);
    }

    [Fact]
    public async Task A_detector_that_exits_clean_without_writing_anything_has_not_produced_evidence()
    {
        Held();

        var result = await Runner(Detector(writesEvidence: false))
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("wrote no evidence", result.Error);
    }

    [Fact]
    public async Task A_failed_detector_says_what_it_said()
    {
        Held();

        var result = await Runner(Detector(exitCode: 1, writesEvidence: false))
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("exited with 1", result.Error);
    }
}
