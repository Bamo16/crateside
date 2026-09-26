using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Tracks;

/// <summary>Produces a fresh <see cref="TracksView"/>. The expensive part, behind a name.</summary>
public delegate Task<TracksView> ReadTracks(CancellationToken cancellationToken);

/// <summary>Holds the composed rows, and decides when they are worth rebuilding.</summary>
public sealed class TracksService : IDisposable
{
    /// <summary>How long to wait for the announcements to stop before rebuilding.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    /// <summary>How often the poll wakes to ask whether the rows have gone stale.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(1);

    /// <summary>Tracks already announced as ready to warp, so each is announced once.</summary>
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

    private readonly ReadTracks _read;
    private readonly Func<TimeSpan> _freshness;
    private readonly StageHub _hub;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly ITimer _poll;

    /// <summary>Whether a read has landed yet, which is what makes the first one different.</summary>
    private bool _seeded;

    /// <param name="freshness">
    /// How stale the rows may get with nothing announced. Zero turns the poll off.
    /// </param>
    public TracksService(
        ReadTracks read,
        StageHub hub,
        TimeProvider? time = null,
        Func<TimeSpan>? freshness = null
    )
    {
        _read = read;
        _freshness = freshness ?? (() => TimeSpan.Zero);
        _hub = hub;
        _time = time ?? TimeProvider.System;

        _timer = _time.CreateTimer(
            _ => _ = RefreshAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );

        _poll = _time.CreateTimer(_ => _ = PollAsync(), null, Heartbeat, Heartbeat);

        _hub.Changed += OnStageChanged;
    }

    /// <summary>Raised once a rebuild has landed, on whatever thread finished it.</summary>
    public event Action? Changed;

    /// <summary>Raised the first time a track is seen to be ready to warp, once per track.</summary>
    public event Action<TrackRow>? ReadyToWarp;

    /// <summary>The rows as last built, or null before the first read.</summary>
    public TracksView? Current { get; private set; }

    /// <summary>When the rows were last rebuilt, whether or not that rebuild succeeded.</summary>
    public DateTimeOffset? LastReadAt { get; private set; }

    /// <summary>Rebuilds if the rows are older than <paramref name="maxAge"/>.</summary>
    public Task EnsureFreshAsync(TimeSpan maxAge, CancellationToken cancellationToken = default) =>
        Current is not null && LastReadAt is { } read && _time.GetUtcNow() - read < maxAge
            ? Task.CompletedTask
            : RefreshAsync(cancellationToken);

    /// <summary>Why the last rebuild failed, or null if it did not.</summary>
    public string? LastError { get; private set; }

    public void Dispose()
    {
        _hub.Changed -= OnStageChanged;
        _timer.Dispose();
        _poll.Dispose();
    }

    /// <summary>Rebuilds now, whatever the timer was going to do.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Current = await _read(cancellationToken);
            LastError = null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            LastError = e.Message;
        }

        // Stamped on failure too.
        LastReadAt = _time.GetUtcNow();

        if (Current is { } current)
            Announce(current);

        Changed?.Invoke();
    }

    /// <summary>Says which tracks have newly become the user's turn, and remembers saying it.</summary>
    private void Announce(TracksView view)
    {
        List<TrackRow> fresh;

        lock (_announced)
        {
            var ready = view.Rows.Where(row => row.State is TrackState.ReadyToWarp).ToList();

            if (!_seeded)
            {
                _seeded = true;

                foreach (var row in ready)
                    _announced.Add(row.VideoId);

                return;
            }

            // A track leaving ready-to-warp and returning counts as arriving again — e.g. after a
            // re-cut.
            _announced.IntersectWith(ready.Select(row => row.VideoId));

            fresh = [.. ready.Where(row => _announced.Add(row.VideoId))];
        }

        foreach (var row in fresh)
            ReadyToWarp?.Invoke(row);
    }

    private Task PollAsync() =>
        _freshness() is { Ticks: > 0 } freshness ? EnsureFreshAsync(freshness) : Task.CompletedTask;

    /// <summary>Restarts the quiet period, so a burst of announcements costs one rebuild.</summary>
    private void OnStageChanged(StageChange change) =>
        _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
}
