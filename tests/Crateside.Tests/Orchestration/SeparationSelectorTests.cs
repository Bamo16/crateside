using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

public class SeparationSelectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static readonly KeyLedger NoLedger = KeyLedger.Read("does-not-exist");

    /// <summary>A queue whose separation work list is these tracks: judged, no stems, not held.</summary>
    private static QueueView View(params QueueItem[] awaitingSeparation) =>
        new()
        {
            Items = awaitingSeparation,
            Outstanding = [],
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = awaitingSeparation,
        };

    private KeyLedger JudgedAs(string confidence, params string[] videoIds)
    {
        var path = Path.Combine(_directory, "runs.jsonl");

        Directory.CreateDirectory(_directory);
        File.WriteAllLines(path, videoIds.Select(v => Line(v, confidence)));

        return KeyLedger.Read(path);
    }

    private static string Line(string videoId, string confidence) =>
        JsonSerializer.Serialize(
            new
            {
                timestamp = "2026-08-16T00:00:00+00:00",
                song = $"artist - {videoId}",
                artist = "Artist",
                title = videoId,
                source_url = $"https://music.youtube.com/watch?v={videoId}",
                verdict = new { key = confidence == "WITHHELD" ? null : "C major", confidence },
            }
        );

    private static QueueItem Item(string videoId, bool unavailable = false) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = $"Track {videoId}",
            IsUnavailable = unavailable,
        };

    [Fact]
    public void An_empty_queue_yields_nothing()
    {
        Assert.False(new SeparationSelector(Jobs).TryPickNext(View(), NoLedger, out _));
    }

    [Fact]
    public void A_track_with_no_verdict_cannot_be_picked_because_it_is_not_awaiting_separation()
    {
        // The guard is structural: an unjudged track never reaches the work list.
        var unjudged = Item("aaaaaaaaaaa");

        var view = new QueueView
        {
            Items = [unjudged],
            Outstanding = [unjudged],
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [],
        };

        Assert.False(new SeparationSelector(Jobs).TryPickNext(view, NoLedger, out _));
    }

    [Fact]
    public void The_first_track_awaiting_separation_is_picked()
    {
        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            NoLedger,
            out var order
        );

        Assert.True(picked);
        Assert.Equal("aaaaaaaaaaa", order.VideoId);
    }

    [Fact]
    public void A_track_we_already_separated_is_not_separated_again()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Succeeded);

        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            NoLedger,
            out var order
        );

        Assert.True(picked);
        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_track_already_in_flight_is_skipped(JobStatus status)
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, status);

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            NoLedger,
            out var order
        );

        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Fact]
    public void A_failure_is_retried()
    {
        Jobs.Record(
            "aaaaaaaaaaa",
            JobStage.Separation,
            JobStatus.Failed,
            error: "cuda out of memory"
        );

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            NoLedger,
            out var order
        );

        Assert.Equal("aaaaaaaaaaa", order.VideoId);
    }

    [Fact]
    public void A_track_that_keeps_failing_is_eventually_left_alone()
    {
        for (var i = 0; i < SeparationSelector.DefaultMaxStrikes; i++)
            Jobs.Record(
                "aaaaaaaaaaa",
                JobStage.Separation,
                JobStatus.Failed,
                error: "cuda out of memory"
            );

        Assert.False(
            new SeparationSelector(Jobs).TryPickNext(View(Item("aaaaaaaaaaa")), NoLedger, out _)
        );
    }

    [Fact]
    public void Giving_up_on_one_track_does_not_block_the_rest_of_the_queue()
    {
        for (var i = 0; i < SeparationSelector.DefaultMaxStrikes; i++)
            Jobs.Record(
                "aaaaaaaaaaa",
                JobStage.Separation,
                JobStatus.Failed,
                error: "cuda out of memory"
            );

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa"), Item("bbbbbbbbbbb")),
            NoLedger,
            out var order
        );

        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Fact]
    public void A_re_cut_asked_for_after_the_last_success_puts_it_back_in_line()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Succeeded);

        var item = Item("aaaaaaaaaaa");

        Assert.False(new SeparationSelector(Jobs).TryPickNext(View(item), NoLedger, out _));

        var asked = item with { CutRequestedAt = DateTimeOffset.UtcNow.AddMinutes(1) };

        Assert.True(new SeparationSelector(Jobs).TryPickNext(View(asked), NoLedger, out _));
    }

    [Fact]
    public void Cancelling_puts_a_track_back_in_line_rather_than_striking_it()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Cancelled);

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            NoLedger,
            out var order
        );

        Assert.Equal("aaaaaaaaaaa", order.VideoId);
    }

    [Fact]
    public void An_upstream_deleted_video_is_never_attempted()
    {
        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa", unavailable: true), Item("bbbbbbbbbbb")),
            NoLedger,
            out var order
        );

        Assert.Equal("bbbbbbbbbbb", order.VideoId);
    }

    [Fact]
    public void The_strike_limit_is_configurable_for_a_track_that_deserves_more_patience()
    {
        Jobs.Record("aaaaaaaaaaa", JobStage.Separation, JobStatus.Failed, error: "transient");

        Assert.False(
            new SeparationSelector(Jobs, maxStrikes: 1).TryPickNext(
                View(Item("aaaaaaaaaaa")),
                NoLedger,
                out _
            )
        );
        Assert.True(
            new SeparationSelector(Jobs, maxStrikes: 2).TryPickNext(
                View(Item("aaaaaaaaaaa")),
                NoLedger,
                out _
            )
        );
    }

    [Fact]
    public void A_withheld_verdict_still_separates_into_unresolved_key()
    {
        // WITHHELD is a verdict, and Unresolved Key is for exactly that.
        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            JudgedAs("WITHHELD", "aaaaaaaaaaa"),
            out var order
        );

        Assert.True(picked);
        Assert.Equal(StemDestination.UnresolvedKey, order.Destination);
    }

    [Fact]
    public void A_taggable_verdict_separates_into_key_identified()
    {
        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaaaaaaaaaa")),
            JudgedAs("HIGH", "aaaaaaaaaaa"),
            out var order
        );

        Assert.Equal(StemDestination.KeyIdentified, order.Destination);
    }
}
