using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Orchestration.Evidence;

public sealed class EvidenceSelectorTests : IDisposable
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

    private void GiveEvidence(string trackKey)
    {
        GiveAudio(trackKey);
        File.WriteAllText(Set.EvidencePathFor(trackKey), "{}");
    }

    private void GiveAudio(string trackKey) =>
        File.WriteAllText(Path.Combine(Set.Prepare(trackKey), "track.flac"), "audio");

    private static WorkItem Item(string trackKey, bool unavailable = false) =>
        new()
        {
            PlaylistItemId = $"PLI-{trackKey}",
            TrackKey = trackKey,
            Title = $"Track {trackKey}",
            IsUnavailable = unavailable,
        };

    /// <summary>Every item arrives with audio held for it.</summary>
    private WorkListsView View(params WorkItem[] outstanding)
    {
        foreach (var item in outstanding)
            GiveAudio(item.TrackKey);

        return new WorkListsView
        {
            Items = outstanding,
            Outstanding = outstanding,
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };
    }

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
        Assert.Equal("aaaaaaaaaaa", next.TrackKey);
    }

    [Fact]
    public void A_track_that_already_has_evidence_is_not_detected_again()
    {
        // Detection records nothing in the ledger, so a detected track is still outstanding.
        GiveEvidence("aaaaaaaaaaa");

        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
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

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_source_still_being_downloaded_is_left_until_the_download_ends(JobStatus status)
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Download, status);

        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
    }

    [Fact]
    public void A_finished_download_is_detected()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Download, JobStatus.Running);
        Jobs.Record("aaaaaaaaaaa", JobStage.Download, JobStatus.Succeeded);

        new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out var next);

        Assert.Equal("aaaaaaaaaaa", next.TrackKey);
    }

    [Fact]
    public void A_succeeded_run_whose_evidence_has_gone_is_acquired_again()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Evidence, JobStatus.Succeeded);

        Assert.True(
            new EvidenceSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), out var next)
        );
        Assert.Equal("aaaaaaaaaaa", next.TrackKey);
    }

    [Fact]
    public void A_track_whose_audio_has_not_arrived_is_not_this_stages_yet()
    {
        var waiting = Item("aaaaaaaaaaa");

        var view = View(waiting);

        Directory.Delete(Set.Prepare(waiting.TrackKey), recursive: true);

        Assert.False(new EvidenceSelector(Jobs, Set).TryPickNext(view, out _));
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
        Assert.True(EvidenceSelector.DefaultMaxStrikes > SeparationSelector.DefaultMaxStrikes);
    }

    [Fact]
    public void An_upstream_deleted_video_is_never_attempted()
    {
        new EvidenceSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa", unavailable: true), Item("bbbbbbbbbbb")),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.TrackKey);
    }
}
