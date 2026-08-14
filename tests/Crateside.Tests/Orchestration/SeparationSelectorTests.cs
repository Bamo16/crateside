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
            Stalled = [],
        };

    [Fact]
    public void An_empty_queue_yields_nothing()
    {
        Assert.False(new SeparationSelector(Jobs).TryPickNext(View(), NoLedger, out _));
    }

    [Fact]
    public void The_first_outstanding_track_is_picked()
    {
        // Outstanding is already oldest-added first, so first is the queue order.
        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaa"), Item("bbb")),
            NoLedger,
            out var order
        );

        Assert.True(picked);
        Assert.Equal("aaa", order.VideoId);
    }

    [Fact]
    public void A_track_we_already_separated_is_not_separated_again()
    {
        // This is the one that makes draining terminate: a separated track stays "outstanding"
        // until it also has a verdict, so the queue alone would loop on it forever.
        Jobs.Record("aaa", JobStage.Separation, JobStatus.Succeeded);

        var picked = new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaa"), Item("bbb")),
            NoLedger,
            out var order
        );

        Assert.True(picked);
        Assert.Equal("bbb", order.VideoId);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    public void A_track_already_in_flight_is_skipped(JobStatus status)
    {
        Jobs.Record("aaa", JobStage.Separation, status);

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaa"), Item("bbb")),
            NoLedger,
            out var order
        );

        Assert.Equal("bbb", order.VideoId);
    }

    [Fact]
    public void A_failure_is_retried()
    {
        Jobs.Record("aaa", JobStage.Separation, JobStatus.Failed, error: "cuda out of memory");

        new SeparationSelector(Jobs).TryPickNext(View(Item("aaa")), NoLedger, out var order);

        Assert.Equal("aaa", order.VideoId);
    }

    [Fact]
    public void A_track_that_keeps_failing_is_eventually_left_alone()
    {
        for (var i = 0; i < SeparationSelector.DefaultMaxStrikes; i++)
            Jobs.Record("aaa", JobStage.Separation, JobStatus.Failed, error: "cuda out of memory");

        Assert.False(new SeparationSelector(Jobs).TryPickNext(View(Item("aaa")), NoLedger, out _));
    }

    [Fact]
    public void Giving_up_on_one_track_does_not_block_the_rest_of_the_queue()
    {
        for (var i = 0; i < SeparationSelector.DefaultMaxStrikes; i++)
            Jobs.Record("aaa", JobStage.Separation, JobStatus.Failed, error: "cuda out of memory");

        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaa"), Item("bbb")),
            NoLedger,
            out var order
        );

        Assert.Equal("bbb", order.VideoId);
    }

    [Fact]
    public void Cancelling_puts_a_track_back_in_line_rather_than_striking_it()
    {
        Jobs.Record("aaa", JobStage.Separation, JobStatus.Cancelled);

        new SeparationSelector(Jobs).TryPickNext(View(Item("aaa")), NoLedger, out var order);

        Assert.Equal("aaa", order.VideoId);
    }

    [Fact]
    public void An_upstream_deleted_video_is_never_attempted()
    {
        // It can never succeed, so trying would burn its three strikes for nothing.
        new SeparationSelector(Jobs).TryPickNext(
            View(Item("aaa", unavailable: true), Item("bbb")),
            NoLedger,
            out var order
        );

        Assert.Equal("bbb", order.VideoId);
    }

    [Fact]
    public void The_strike_limit_is_configurable_for_a_track_that_deserves_more_patience()
    {
        Jobs.Record("aaa", JobStage.Separation, JobStatus.Failed, error: "transient");

        Assert.False(
            new SeparationSelector(Jobs, maxStrikes: 1).TryPickNext(
                View(Item("aaa")),
                NoLedger,
                out _
            )
        );
        Assert.True(
            new SeparationSelector(Jobs, maxStrikes: 2).TryPickNext(
                View(Item("aaa")),
                NoLedger,
                out _
            )
        );
    }
}
