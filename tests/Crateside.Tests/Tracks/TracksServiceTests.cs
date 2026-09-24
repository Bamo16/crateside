using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Tracks;

/// <summary>The cache in front of the most expensive thing the app does.</summary>
public class TracksServiceTests
{
    private static TracksView Empty => new() { Rows = [] };

    private static StageChange Change(JobStage stage = JobStage.Separation) =>
        new() { Stage = stage, VideoId = "aaaaaaaaaaa" };

    [Fact]
    public async Task A_stage_change_refreshes_once_the_burst_has_settled()
    {
        var time = new FakeTimeProvider();
        var hub = new StageHub();
        var reads = 0;

        using var service = new TracksService(
            _ =>
            {
                reads++;
                return Task.FromResult(Empty);
            },
            hub,
            time
        );

        var refreshed = new TaskCompletionSource();
        service.Changed += () => refreshed.TrySetResult();

        hub.Publish(Change());

        Assert.Equal(0, reads);

        time.Advance(TimeSpan.FromSeconds(5));
        await refreshed.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, reads);
        Assert.NotNull(service.Current);
    }

    [Fact]
    public async Task A_whole_track_crossing_the_pipeline_costs_one_rebuild()
    {
        // Ten announcements: Running then Succeeded, for each of five stages.
        var time = new FakeTimeProvider();
        var hub = new StageHub();
        var reads = 0;

        using var service = new TracksService(
            _ =>
            {
                reads++;
                return Task.FromResult(Empty);
            },
            hub,
            time
        );

        var refreshed = new TaskCompletionSource();
        service.Changed += () => refreshed.TrySetResult();

        foreach (var stage in Enum.GetValues<JobStage>())
        {
            hub.Publish(Change(stage));
            hub.Publish(Change(stage));

            // Each arrives inside the quiet period, which restarts it.
            time.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(0, reads);

        time.Advance(TimeSpan.FromSeconds(5));
        await refreshed.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, reads);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task Fresh_enough_rows_are_not_rebuilt_but_stale_ones_are()
    {
        var time = new FakeTimeProvider();
        var reads = 0;

        using var service = new TracksService(
            _ =>
            {
                reads++;
                return Task.FromResult(Empty);
            },
            new StageHub(),
            time
        );

        await service.EnsureFreshAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, reads);

        time.Advance(TimeSpan.FromSeconds(5));
        await service.EnsureFreshAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, reads);

        time.Advance(TimeSpan.FromSeconds(30));
        await service.EnsureFreshAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_disposed_service_stops_listening()
    {
        var time = new FakeTimeProvider();
        var hub = new StageHub();
        var reads = 0;

        var service = new TracksService(
            _ =>
            {
                reads++;
                return Task.FromResult(Empty);
            },
            hub,
            time
        );

        service.Dispose();
        hub.Publish(Change());
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, reads);
    }
}
