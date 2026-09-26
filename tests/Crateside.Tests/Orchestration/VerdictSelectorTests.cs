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
        GiveEvidence("aaaaaaaaaaa");
        Jobs.Record("aaaaaaaaaaa", JobStage.Verdict, JobStatus.Succeeded);

        Assert.True(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void A_track_that_keeps_failing_is_eventually_left_alone()
    {
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
        GiveEvidence("aaaaaaaaaaa");

        Assert.True(
            new VerdictSelector(Jobs, Set).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    private KeyLedger SouthSide()
    {
        var path = Path.Combine(_directory, "south-side.jsonl");

        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-10T00:00:00+00:00",
                    song = "moby - south side (feat. gwen stefani)",
                    source_url = "https://music.youtube.com/watch?v=GXKg0sNTKXE",
                    verdict = new { key = "C major", confidence = "HIGH" },
                }
            )
        );

        return KeyLedger.Read(path);
    }

    [Fact]
    public void A_track_that_looks_like_a_judged_song_waits_for_the_user()
    {
        GiveEvidence("jWj-UVA1304");

        var item = Item("jWj-UVA1304") with { Title = "Moby - South Side (Official Video)" };

        Assert.False(new VerdictSelector(Jobs, Set).TryPickNext(View(item), SouthSide(), out _));
    }

    [Fact]
    public void Saying_it_is_a_different_song_lets_it_be_judged()
    {
        GiveEvidence("jWj-UVA1304");

        var item = Item("jWj-UVA1304") with
        {
            Title = "Moby - South Side",
            DifferentFrom = "moby - south side (feat. gwen stefani)",
        };

        Assert.True(new VerdictSelector(Jobs, Set).TryPickNext(View(item), SouthSide(), out _));
    }

    [Fact]
    public void The_resolved_name_is_asked_before_the_playlist_title()
    {
        GiveEvidence("jWj-UVA1304");

        var item = Item("jWj-UVA1304") with { Title = "some upload" };

        Assert.True(new VerdictSelector(Jobs, Set).TryPickNext(View(item), SouthSide(), out _));
        Assert.False(
            new VerdictSelector(Jobs, Set, resolvedName: _ => "Moby - South Side").TryPickNext(
                View(item),
                SouthSide(),
                out _
            )
        );
    }

    [Fact]
    public void Judgment_is_no_more_patient_than_separation()
    {
        Assert.Equal(SeparationSelector.DefaultMaxStrikes, VerdictSelector.DefaultMaxStrikes);
        Assert.True(VerdictSelector.DefaultMaxStrikes < EvidenceSelector.DefaultMaxStrikes);
    }

    [Fact]
    public void The_library_backfill_is_left_to_the_idle_window()
    {
        GiveEvidence("aaaaaaaaaaa");
        GiveEvidence("bbbbbbbbbbb");

        new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa") with { IsBackfill = true }, Item("bbbbbbbbbbb")),
            NoLedger,
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Fact]
    public void A_backfill_track_the_user_asked_for_first_is_judged_now()
    {
        GiveEvidence("aaaaaaaaaaa");

        var picked = new VerdictSelector(Jobs, Set).TryPickNext(
            View(Item("aaaaaaaaaaa") with { IsBackfill = true }) with
            {
                Bumped = "aaaaaaaaaaa",
            },
            NoLedger,
            out _
        );

        Assert.True(picked);
    }

    [Fact]
    public void An_idle_window_judges_only_the_backfill()
    {
        GiveEvidence("aaaaaaaaaaa");
        GiveEvidence("bbbbbbbbbbb");

        new VerdictSelector(Jobs, Set).TryPickBackfill(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb") with { IsBackfill = true }),
            NoLedger,
            out var next
        );

        Assert.Equal("bbbbbbbbbbb", next.VideoId);
    }

    [Fact]
    public void An_idle_window_with_no_backfill_waiting_judges_nothing()
    {
        GiveEvidence("aaaaaaaaaaa");

        Assert.False(
            new VerdictSelector(Jobs, Set).TryPickBackfill(
                View(Item("aaaaaaaaaaa")),
                NoLedger,
                out _
            )
        );
    }
}
