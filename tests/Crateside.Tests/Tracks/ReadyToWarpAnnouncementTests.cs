using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Tracks;

/// <summary>
/// The one interruption the page makes: a track has become the user's to finish.
/// </summary>
/// <remarks>
/// In the singleton rather than in the page because a page is a circuit and a circuit comes and
/// goes — SignalR drops and reconnects, and a second tab is a second subscriber. Announcing from
/// there would announce the same track again on every one of those.
/// </remarks>
public class ReadyToWarpAnnouncementTests
{
    private static TrackRow Ready(string videoId) =>
        new()
        {
            VideoId = videoId,
            Title = $"Track {videoId}",
            Samples = [new TrackSample { Label = "Vocal - Full", AbletonKey = "C minor" }],
        };

    private static TrackRow Queued(string videoId) =>
        new() { VideoId = videoId, Title = $"Track {videoId}" };

    private static TracksView View(params TrackRow[] rows) => new() { Rows = rows };

    /// <summary>A service whose successive reads return the given views in order.</summary>
    private static TracksService Reading(params TracksView[] views)
    {
        var read = 0;

        return new TracksService(
            _ => Task.FromResult(views[Math.Min(read++, views.Length - 1)]),
            new StageHub(),
            new FakeTimeProvider()
        );
    }

    [Fact]
    public async Task The_first_read_announces_nothing()
    {
        // It is the state of the world, not a list of things that just happened. A track rests in
        // ready-to-warp for weeks, so announcing what is already there would be a wall of toasts
        // on every start.
        using var service = Reading(View(Ready("a"), Ready("b")));
        var announced = new List<string>();

        service.ReadyToWarp += row => announced.Add(row.VideoId);
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Empty(announced);
    }

    [Fact]
    public async Task A_track_that_becomes_ready_is_announced()
    {
        using var service = Reading(View(Queued("a")), View(Ready("a")));
        var announced = new List<string>();

        service.ReadyToWarp += row => announced.Add(row.VideoId);

        await service.RefreshAsync(TestContext.Current.CancellationToken);
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a"], announced);
    }

    [Fact]
    public async Task It_is_announced_once_however_many_times_the_rows_are_rebuilt()
    {
        // A track sits in this state for weeks and the rows are rebuilt on every stage change, so
        // "still ready" has to be silent or the toast becomes the page's background noise.
        using var service = Reading(View(Queued("a")), View(Ready("a")));
        var announced = new List<string>();

        service.ReadyToWarp += row => announced.Add(row.VideoId);

        for (var i = 0; i < 4; i++)
            await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a"], announced);
    }

    [Fact]
    public async Task Only_the_track_that_just_arrived_is_announced()
    {
        using var service = Reading(
            View(Queued("a"), Queued("b")),
            View(Ready("a"), Queued("b")),
            View(Ready("a"), Ready("b"))
        );
        var announced = new List<string>();

        service.ReadyToWarp += row => announced.Add(row.VideoId);

        await service.RefreshAsync(TestContext.Current.CancellationToken);
        await service.RefreshAsync(TestContext.Current.CancellationToken);
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], announced);
    }

    [Fact]
    public async Task A_read_that_failed_seeds_nothing()
    {
        // The first read is only special because it says what was already there. One that threw
        // said nothing, so the next successful read is still the first.
        using var service = new TracksService(
            _ => throw new IOException("the library is not mounted"),
            new StageHub(),
            new FakeTimeProvider()
        );
        var announced = new List<string>();

        service.ReadyToWarp += row => announced.Add(row.VideoId);
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(service.LastError);
        Assert.Empty(announced);
    }
}
