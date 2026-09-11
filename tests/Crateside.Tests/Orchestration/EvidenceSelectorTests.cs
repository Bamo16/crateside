using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

public class EvidenceSelectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private void GiveEvidence(string videoId)
    {
        Set.Prepare(videoId);
        File.WriteAllText(Set.EvidencePathFor(videoId), "{}");
    }

    private static QueueItem Item(string videoId, bool unavailable = false) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = $"Track {videoId}",
            IsUnavailable = unavailable,
        };

    private static QueueView View(params QueueItem[] outstanding) =>
        new()
        {
            Items = outstanding,
            Outstanding = outstanding,
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };

    [Fact]
    public void An_empty_queue_yields_nothing() =>
        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(View(), out _));

    [Fact]
    public void The_first_outstanding_track_is_picked()
    {
        var picked = new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.True(picked);
        Assert.Equal("aaaaaaaaaaa", next.VideoId);
    }

    [Fact]
    public void A_track_that_already_has_evidence_is_not_detected_again()
    {
        // This is the one that makes the stage terminate. Detection records nothing in the
        // ledger, so a detected track is still outstanding -- without this the same track would
        // be picked forever and nothing else would ever run.
        GiveEvidence("aaaaaaaaaaa");

        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_track_already_in_flight_is_skipped(JobStatus status)
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, status);

        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Fact]
    public void A_succeeded_run_whose_evidence_has_gone_is_acquired_again()
    {
        // The working set is scratch and may be deleted by hand at any time. Losing it costs a
        // re-run, so a past success is not a reason to refuse one.
        Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, JobStatus.Succeeded);

        Assert.True(
            new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out var next)
        );
        Assert.Equal("aaaaaaaaaaa", next.VideoId);
    }

    [Fact]
    public void A_failure_is_retried()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, JobStatus.Failed, error: "download failed");

        Assert.True(new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out _));
    }

    [Fact]
    public void A_track_that_keeps_failing_is_eventually_left_alone()
    {
        for (var i = 0; i < EvidenceSelector.DefaultMaxStrikes; i++)
            Jobs.Record(
                "aaaaaaaaaaa",
                JobStage.Evidence,
                JobStatus.Failed,
                error: "download failed"
            );

        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out _));
    }

    [Fact]
    public void Evidence_is_more_patient_than_separation()
    {
        // A download and some CPU, against GPU hours -- and its usual failures are the
        // retryable kind. Being given up on as fast as separation would be wrong.
        Assert.True(EvidenceSelector.DefaultMaxStrikes > SeparationSelector.DefaultMaxStrikes);
    }

    [Fact]
    public void An_upstream_deleted_video_is_never_attempted()
    {
        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa", unavailable: true), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }
}
