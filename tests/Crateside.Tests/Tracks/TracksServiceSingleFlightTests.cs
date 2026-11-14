using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

public sealed class TracksServiceSingleFlightTests
{
    [Fact]
    public async Task Workers_asking_for_fresh_rows_at_once_share_one_read()
    {
        var reader = new GatedReader();
        using var service = new TracksService(reader, new StageHub());

        var asks = Enumerable
            .Range(0, 3)
            .Select(_ =>
                service.EnsureFreshAsync(
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken
                )
            )
            .ToArray();

        await reader.Started(1);
        reader.Finish();
        await Task.WhenAll(asks).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task A_refresh_during_a_read_waits_for_one_follow_up_read_shared_with_the_others()
    {
        var reader = new GatedReader();
        using var service = new TracksService(reader, new StageHub());

        var first = service.RefreshAsync(TestContext.Current.CancellationToken);
        await reader.Started(1);

        var later = new[]
        {
            service.RefreshAsync(TestContext.Current.CancellationToken),
            service.RefreshAsync(TestContext.Current.CancellationToken),
        };

        reader.Finish();
        await first.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.All(later, task => Assert.False(task.IsCompleted));

        await reader.Started(2);
        reader.Finish();
        await Task.WhenAll(later).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(2, reader.Reads);
    }

    [Fact]
    public async Task A_caller_that_gives_up_leaves_the_read_to_the_others()
    {
        var reader = new GatedReader();
        using var service = new TracksService(reader, new StageHub());
        using var cancel = new CancellationTokenSource();

        var giver = service.RefreshAsync(cancel.Token);
        var stayer = service.EnsureFreshAsync(
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );
        await reader.Started(1);

        await cancel.CancelAsync();
        await giver.WaitAsync(Wait, TestContext.Current.CancellationToken);

        reader.Finish();
        await stayer.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.NotNull(service.Current);
    }

    private static TimeSpan Wait => TimeSpan.FromSeconds(10);

    private sealed class GatedReader : ITracksReader
    {
        private readonly SemaphoreSlim _release = new(0);
        private readonly SemaphoreSlim _started = new(0);

        public int Reads { get; private set; }

        public async Task<TracksView> ReadAsync(CancellationToken ct = default)
        {
            Reads++;
            _started.Release();
            await _release.WaitAsync(ct);

            return new TracksView { Rows = [] };
        }

        public void Finish() => _release.Release();

        public async Task Started(int count)
        {
            for (var i = 0; i < count; i++)
                await _started.WaitAsync(Wait, TestContext.Current.CancellationToken);
        }
    }
}
