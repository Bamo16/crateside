namespace Crateside.Core.Orchestration;

/// <summary>
/// The one place a stage says that something happened to a track.
/// </summary>
/// <remarks>
/// <para>
/// Stages published nothing, which is the whole reason the app looked like it needed a refresh.
/// <see cref="DrainControl"/> already raised <c>Changed</c> for pausing and for the activity
/// line, so the header kept up while the list under it went stale — the missing piece was a
/// notification when a track actually moved on, not a different framework.
/// </para>
/// <para>
/// Unlike <see cref="DrainControl.Changed"/>, this carries a payload. That control is
/// level-triggered — "something is different, go and look" — which is right for a header showing
/// one current activity. A list of thirty rows wants to know <em>which</em> track moved, so it
/// can refresh one row instead of re-reading the playlist and rescanning both stem roots.
/// </para>
/// </remarks>
public sealed class StageHub
{
    /// <summary>Raised on the publishing thread whenever a stage finishes with a track.</summary>
    /// <remarks>
    /// This is a background worker's thread, never the browser's. A Blazor subscriber has to
    /// marshal onto its circuit with <c>InvokeAsync</c> before touching component state.
    /// </remarks>
    public event Action<StageChange>? Changed;

    /// <summary>
    /// Tells every subscriber, and lets none of them stop the rest or the caller.
    /// </summary>
    /// <remarks>
    /// A plain <c>Changed?.Invoke(...)</c> runs the handlers in registration order and abandons
    /// the list at the first exception, so one page that had gone away would silence every other
    /// subscriber and end the drain loop's tick. The publisher here is a background worker with
    /// real work in flight, and the subscribers are browser circuits that come and go — the
    /// asymmetry is the argument for swallowing.
    /// </remarks>
    public void Publish(StageChange change)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<StageChange>)handler)(change);
            }
            catch (Exception)
            {
                // Deliberately not rethrown and deliberately not logged: Core takes no logging
                // dependency, and a subscriber's failure is the subscriber's to notice. The same
                // reasoning as the job store's unreadable-file catch — losing a notification
                // costs a stale row until the next tick, where letting it through costs a stage.
            }
        }
    }
}

/// <summary>What one stage did to one track.</summary>
public sealed record StageChange
{
    public required JobStage Stage { get; init; }

    /// <summary>
    /// The track it happened to, where the stage knows. Handoff reads the disk rather than the
    /// queue, so it can finish work for a track it never learned the video ID of.
    /// </summary>
    public string? VideoId { get; init; }
}
