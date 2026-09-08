using Crateside.Core.Orchestration;

namespace Crateside.Core.Tracks;

/// <summary>Produces a fresh <see cref="TracksView"/>. The expensive part, behind a name.</summary>
public delegate Task<TracksView> ReadTracks(CancellationToken cancellationToken);

/// <summary>
/// Holds the composed rows, and decides when they are worth rebuilding.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A row update is cheap and a full read is not.</strong> One read calls the playlist API
/// and walks three stem roots, opening every audio file under them with TagLib. Now that the
/// stages announce every job they record, a single track crossing five stages publishes ten
/// changes — so reacting to each one directly would mean ten walks of the library for one track.
/// </para>
/// <para>
/// The answer is a quiet period rather than a fixed interval. Stage changes arrive in bursts, two
/// at a time per stage, and what matters is refreshing once the burst has settled rather than on
/// a schedule that is either too slow to feel live or too fast to be affordable.
/// </para>
/// </remarks>
public sealed class TracksService : IDisposable
{
    /// <summary>
    /// How long to wait for the announcements to stop before rebuilding.
    /// </summary>
    /// <remarks>
    /// Long enough to swallow a stage's Running-then-Succeeded pair and the next stage picking the
    /// track straight up; short enough that the page feels like it is watching rather than
    /// polling. Not measured — chosen, and worth revisiting against a real drain.
    /// </remarks>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often the poll wakes to ask whether the rows have gone stale.
    /// </summary>
    /// <remarks>
    /// A heartbeat rather than the interval itself, so changing the setting takes effect within a
    /// minute instead of at the next restart. Waking to find nothing to do costs nothing.
    /// </remarks>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Tracks already announced as ready to warp, so each is announced once.
    /// </summary>
    /// <remarks>
    /// Here rather than on the page because a page is a circuit, and a circuit comes and goes:
    /// SignalR drops and reconnects, the user opens a second tab, and every one of those would be
    /// a fresh chance to announce the same track again. The set outlives all of them.
    /// </remarks>
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

    private readonly ReadTracks _read;
    private readonly Func<TimeSpan> _freshness;
    private readonly StageHub _hub;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly ITimer _poll;

    /// <summary>
    /// Whether a read has landed yet, which is what makes the first one different.
    /// </summary>
    /// <remarks>
    /// The first read is the state of the world rather than a list of things that just happened. A
    /// track rests in <em>ready to warp</em> for weeks, so announcing what is already there would
    /// mean a wall of toasts every time the app starts.
    /// </remarks>
    private bool _seeded;

    /// <param name="freshness">
    /// How stale the rows may get with nothing announced — read on every beat rather than captured,
    /// because it is a user setting and changing it should not need a restart. Zero turns the poll
    /// off. A function rather than the settings store, so this still depends on <em>a read</em>
    /// and not on the queue.
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

        // Its own timer rather than a shorter debounce: the debounce is about not paying twice for
        // one burst of announcements, and this is about the things that announce nothing at all.
        _poll = _time.CreateTimer(_ => _ = PollAsync(), null, Heartbeat, Heartbeat);

        _hub.Changed += OnStageChanged;
    }

    /// <summary>Raised once a rebuild has landed, on whatever thread finished it.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised the first time a track is seen to be ready to warp, once per track.
    /// </summary>
    /// <remarks>
    /// The one moment in the pipeline that is worth interrupting for: everything before it is
    /// crateside working, and this is where the track becomes the user's to finish.
    /// </remarks>
    public event Action<TrackRow>? ReadyToWarp;

    /// <summary>The rows as last built, or null before the first read.</summary>
    public TracksView? Current { get; private set; }

    /// <summary>When the rows were last rebuilt, whether or not that rebuild succeeded.</summary>
    public DateTimeOffset? LastReadAt { get; private set; }

    /// <summary>
    /// Rebuilds if the rows are older than <paramref name="maxAge"/>.
    /// </summary>
    /// <remarks>
    /// <strong>Because the most important changes are ones crateside never sees.</strong> The hub
    /// announces work crateside <em>ran</em>; warping is the user in Ableton, dragging files from
    /// <c>Sample Staging</c> to <c>Mashup Samples</c>, which publishes nothing here. Nothing would
    /// ever invalidate the cache for the one view the user opens the page to look at, so arriving
    /// at it has to be a reason to look again.
    /// </remarks>
    public Task EnsureFreshAsync(TimeSpan maxAge, CancellationToken cancellationToken = default) =>
        Current is not null && LastReadAt is { } read && _time.GetUtcNow() - read < maxAge
            ? Task.CompletedTask
            : RefreshAsync(cancellationToken);

    /// <summary>Why the last rebuild failed, or null if it did not.</summary>
    /// <remarks>
    /// Kept rather than thrown away because a background refresh has nobody to throw to, and a
    /// page showing hours-old rows with no indication that reading stopped working is worse than
    /// one saying so. An unreadable playlist is its own state, never an empty list.
    /// </remarks>
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
            // Nowhere to throw: this usually runs from a timer with no caller. Keeping the
            // previous rows is right — they were true a moment ago — provided the page is told
            // that reading stopped working.
            LastError = e.Message;
        }

        // Stamped on a failure too, so a root that has gone away is not retried on every render.
        LastReadAt = _time.GetUtcNow();

        if (Current is { } current)
            Announce(current);

        Changed?.Invoke();
    }

    /// <summary>Says which tracks have newly become the user's turn, and remembers saying it.</summary>
    private void Announce(TracksView view)
    {
        List<TrackRow> fresh;

        // Refreshes overlap: the debounce timer and a page arriving both call in, and the set is
        // the only thing keeping an announcement from being made twice.
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

            // Forgotten the moment a track stops being ready, so coming back counts as
            // arriving. A re-cut is exactly that: its staged files go, separation runs again, and
            // the track becoming the user's turn a second time is news a second time.
            _announced.IntersectWith(ready.Select(row => row.VideoId));

            fresh = [.. ready.Where(row => _announced.Add(row.VideoId))];
        }

        foreach (var row in fresh)
            ReadyToWarp?.Invoke(row);
    }

    /// <summary>Rebuilds if the setting says the rows have gone stale, and does nothing if not.</summary>
    private Task PollAsync() =>
        _freshness() is { Ticks: > 0 } freshness ? EnsureFreshAsync(freshness) : Task.CompletedTask;

    /// <summary>Restarts the quiet period, so a burst of announcements costs one rebuild.</summary>
    private void OnStageChanged(StageChange change) =>
        _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
}
