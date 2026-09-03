using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Tracks;

/// <summary>
/// The cache in front of the most expensive thing the app does.
/// </summary>
/// <remarks>
/// A row update is cheap and a full read is not: it calls the playlist API and walks three stem
/// roots, opening every audio file with TagLib. The stages now announce every job they record, so
/// without a debounce a single track moving through five stages would trigger ten of those walks.
/// </remarks>
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

        // Nothing yet: the point is to let a burst finish before paying for a walk of the library.
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
        // Ten announcements: Running then Succeeded, for each of five stages. Reacting to each
        // directly would walk the library ten times to learn what one walk says.
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

            // Each stage arrives inside the quiet period, which restarts it rather than adding to
            // it — so a long pipeline does not defer the rebuild indefinitely, it just keeps the
            // page quiet while the track is plainly still moving.
            time.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(0, reads);

        time.Advance(TimeSpan.FromSeconds(5));
        await refreshed.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, reads);

        // And it settles rather than repeating.
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void A_disposed_service_stops_listening()
    {
        // The hub outlives any one reader, so a subscription that survived disposal would keep
        // rebuilding for a page nobody is looking at, forever.
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
