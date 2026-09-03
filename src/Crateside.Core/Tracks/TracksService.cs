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

    private readonly ReadTracks _read;
    private readonly StageHub _hub;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;

    public TracksService(ReadTracks read, StageHub hub, TimeProvider? time = null)
    {
        _read = read;
        _hub = hub;
        _time = time ?? TimeProvider.System;

        _timer = _time.CreateTimer(
            _ => _ = RefreshAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );

        _hub.Changed += OnStageChanged;
    }

    /// <summary>Raised once a rebuild has landed, on whatever thread finished it.</summary>
    public event Action? Changed;

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

        Changed?.Invoke();
    }

    /// <summary>Restarts the quiet period, so a burst of announcements costs one rebuild.</summary>
    private void OnStageChanged(StageChange change) =>
        _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
}
