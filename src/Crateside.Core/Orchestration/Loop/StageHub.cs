using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Orchestration.Loop;

/// <summary>
/// The one place a stage says that something happened to a track.
/// </summary>
public sealed class StageHub
{
    /// <summary>
    /// Raised on a background worker's thread whenever a stage finishes with a track; a Blazor
    /// subscriber must <c>InvokeAsync</c> first.
    /// </summary>
    public event Action<StageChange>? Changed;

    /// <summary>
    /// Tells every subscriber, and lets none of them stop the rest or the caller — which a plain
    /// <c>Changed?.Invoke</c> would.
    /// </summary>
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
    [JsonPropertyName("videoId")]
    public string? TrackKey { get; init; }
}
