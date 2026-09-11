using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

public class VerdictSelectorTests : IDisposable
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

    private static readonly KeyLedger NoLedger = KeyLedger.Read("does-not-exist");

    private void GiveEvidence(string videoId)
    {
        Set.Prepare(videoId);
        File.WriteAllText(Set.EvidencePathFor(videoId), "{}");
    }

    private KeyLedger Judged(params string[] videoIds)
    {
        var path = Path.Combine(_directory, "runs.jsonl");

        Directory.CreateDirectory(_directory);
        File.WriteAllLines(
            path,
            videoIds.Select(v =>
                JsonSerializer.Serialize(
                    new
                    {
                        timestamp = "2026-08-17T00:00:00+00:00",
                        song = $"artist - {v}",
                        artist = "Artist",
                        title = v,
                        source_url = $"https://music.youtube.com/watch?v={v}",
                        verdict = new { key = "C major", confidence = "HIGH" },
                    }
                )
            )
        );

        return KeyLedger.Read(path);
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
    public void A_track_with_no_evidence_has_nothing_to_judge()
    {
        // The entry condition. Spending quota to open a session with no input is the expensive
        // way to discover there is nothing to do.
        Assert.False(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void A_detected_track_is_picked()
    {
        GiveEvidence("aaaaaaaaaaa");

        var picked = new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            NoLedger,
            out var next
        );

        Assert.True(picked);
        Assert.Equal("aaaaaaaaaaa", next.VideoId);
    }

    [Fact]
    public void A_track_that_already_has_a_verdict_is_not_judged_again()
    {
        // The exit condition, and the exact negation of what the runner checks afterwards --
        // which is what makes a half-finished session need no special handling.
        GiveEvidence("aaaaaaaaaaa");
        GiveEvidence("bbbbbbbbbbb");

        new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            Judged("aaaaaaaaaaa"),
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Fact]
    public void The_undetected_are_skipped_rather_than_blocking_the_detected()
    {
        // The stages run as independent loops, so a track waiting on the evidence stage must not
        // hold up one that is ready to judge.
        GiveEvidence("bbbbbbbbbbb");

        new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            NoLedger,
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_track_already_in_flight_is_skipped(JobStatus status)
    {
        GiveEvidence("aaaaaaaaaaa");
        GiveEvidence("bbbbbbbbbbb");
        Jobs.Record("aaaaaaaaaaa", JobStage.Verdict, status);

        new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            NoLedger,
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Fact]
    public void A_success_that_recorded_nothing_is_judged_again()
    {
        // The ledger check has already said no verdict exists, so a job claiming success
        // produced nothing that survived. Judging again is the only way that track ever moves.
        GiveEvidence("aaaaaaaaaaa");
        Jobs.Record("aaaaaaaaaaa", JobStage.Verdict, JobStatus.Succeeded);

        Assert.True(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void A_track_that_keeps_failing_is_eventually_left_alone()
    {
        // ADR 0001's point: a poisoned track should stop being retried nightly, because every
        // attempt spends quota that a track which would have succeeded could have had.
        GiveEvidence("aaaaaaaaaaa");

        for (var i = 0; i < VerdictSelector.DefaultMaxStrikes; i++)
            Jobs.Record("aaaaaaaaaaa", JobStage.Verdict, JobStatus.Failed, error: "quota");

        Assert.False(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void An_upstream_deleted_video_is_never_attempted()
    {
        GiveEvidence("aaaaaaaaaaa");
        GiveEvidence("bbbbbbbbbbb");

        new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa", unavailable: true), Item("bbbbbbbbbbb")),
            NoLedger,
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Theory]
    [InlineData(PremiumStatus.NoPremiumLadder)]
    [InlineData(PremiumStatus.SourceHasNoPremiumAudio)]
    public void A_download_short_of_premium_is_not_judged(PremiumStatus status)
    {
        // Before the verdict rather than before the GPU, because a swap copies the verdict
        // forward: a key derived from poor audio would be restated onto the better upload
        // instead of re-derived, and nothing would say so (ADR 0016).
        GiveEvidence("aaaaaaaaaaa");
        GiveEvidence("bbbbbbbbbbb");
        Set.SaveAcquisition("aaaaaaaaaaa", new Acquisition { PremiumStatus = status });

        new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            NoLedger,
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Theory]
    [InlineData(PremiumStatus.NotSignedIn)]
    [InlineData(PremiumStatus.AccountNotPremium)]
    [InlineData(PremiumStatus.Premium)]
    public void A_session_problem_holds_nothing_up(PremiumStatus status)
    {
        // These are true of every download at once. Stopping tracks one at a time for them would
        // halt the whole queue for something no per-track action can fix.
        GiveEvidence("aaaaaaaaaaa");
        Set.SaveAcquisition("aaaaaaaaaaa", new Acquisition { PremiumStatus = status });

        Assert.True(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void The_users_say_so_lets_a_stopped_track_through()
    {
        GiveEvidence("aaaaaaaaaaa");
        Set.SaveAcquisition(
            "aaaaaaaaaaa",
            new Acquisition { PremiumStatus = PremiumStatus.NoPremiumLadder }
        );

        Assert.False(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );

        Set.AcceptAcquisition("aaaaaaaaaaa");

        Assert.True(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void A_track_acquired_before_any_of_this_is_judged_as_before()
    {
        // No record is not a shortfall: every working set already on disk has none.
        GiveEvidence("aaaaaaaaaaa");

        Assert.True(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void Judgment_is_no_more_patient_than_separation()
    {
        // It spends quota, and its usual failures are not the transient kind: a track with no
        // human coverage fails the same way every time.
        Assert.Equal(SeparationSelector.DefaultMaxStrikes, VerdictSelector.DefaultMaxStrikes);
        Assert.True(VerdictSelector.DefaultMaxStrikes < EvidenceSelector.DefaultMaxStrikes);
    }
}
