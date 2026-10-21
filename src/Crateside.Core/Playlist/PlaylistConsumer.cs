using Crateside.Core.WorkLists;

namespace Crateside.Core.Playlist;

/// <summary>
/// Gives back the entries crateside has recorded, so the playlist stops holding what it has
/// already delivered (ADR 0021).
/// </summary>
public sealed class PlaylistConsumer(
    CachedPlaylist playlist,
    PlaylistSettingsStore settings,
    PlaylistAuthorizer authorizer,
    IntakeStore intake,
    TimeProvider time
)
{
    public async Task ConsumeAsync(IReadOnlyList<WorkItem> held, CancellationToken ct = default)
    {
        // Not "recorded by this scan": a track recorded before the setting was turned on would
        // then never be new again, and the playlist would never empty at all.
        // Never on a failed read: what is held is then stale or empty, and an entry offered
        // back is offered only once.
        if (
            !settings.Read().ConsumeOnArrival
            || !authorizer.IsAuthorized
            || !intake.IsDurable
            || playlist.Problem is not null
        )
            return;

        // Marked first, and regardless of what the removal reports: a delete costs fifty quota
        // units against a daily ten thousand, and a failure retried on every scan spends the day.
        var entries = intake.ClaimForRemoval(held, time.GetUtcNow());

        if (entries.Count is 0)
            return;

        var offered = entries.Select(item => item.VideoId).ToList();

        try
        {
            var results = await playlist.RemoveAsync(entries, ct);
            var videoIdOf = entries.ToDictionary(
                item => item.PlaylistItemId!,
                item => item.VideoId,
                StringComparer.Ordinal
            );

            // Kept on the record rather than discarded: nothing will try these again, so this is
            // the only trace that they were left in the playlist, and why (#34).
            intake.RecordRemovalFailures(FailuresByVideo(results, videoIdOf));
        }
        catch (PlaylistUnavailableException)
        {
            // Consent has lapsed, so no delete was attempted and no quota was spent. Swallowed:
            // the playlist read itself worked, and the stages do not need this write (ADR 0026).
            intake.ClearRemovalAttempted(offered);
        }
    }

    /// <summary>One failure per video: a playlist can hold the same video twice, and both entries can fail.</summary>
    internal static Dictionary<string, RemovalFailure> FailuresByVideo(
        IEnumerable<RemovalResult> results,
        IReadOnlyDictionary<string, string> videoIdOf
    ) =>
        results
            .Where(result => !result.Removed && videoIdOf.ContainsKey(result.PlaylistItemId))
            .GroupBy(result => videoIdOf[result.PlaylistItemId], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new RemovalFailure
                {
                    Reason = group.First().Reason ?? "the API gave no reason",
                    CanSucceedLater = group.First().CanSucceedLater,
                },
                StringComparer.Ordinal
            );
}
