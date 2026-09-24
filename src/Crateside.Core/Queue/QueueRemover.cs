using Google;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;

namespace Crateside.Core.Queue;

/// <summary>
/// Deletes items from the Queue playlist. The only part of crateside that writes anything,
/// anywhere.
/// </summary>
/// <remarks>
/// <c>playlistItems.delete</c> costs 50 quota units per call — fifty times a list — against
/// the 10,000/day default.
/// </remarks>
public sealed class QueueRemover(QueueAuthorizer authorizer)
{
    /// <summary>
    /// Removes the given items. Each is attempted independently: one failure does not abandon
    /// the rest, and the caller gets a per-item account of what happened.
    /// </summary>
    /// <remarks>
    /// An item already gone from the playlist reports as removed rather than as an error, so a
    /// retry after partial failure is safe.
    /// </remarks>
    public async Task<IReadOnlyList<RemovalResult>> RemoveAsync(
        IReadOnlyList<QueueItem> items,
        CancellationToken cancellationToken = default
    )
    {
        if (items.Count == 0)
            return [];

        if (await authorizer.GetCredentialAsync(cancellationToken) is not { } credential)
            throw new QueueUnavailableException(
                "Not authorized to modify the playlist. Grant access and try again."
            );

        using var service = new YouTubeService(
            new BaseClientService.Initializer
            {
                ApplicationName = "crateside",
                HttpClientInitializer = credential,
            }
        );

        var results = new List<RemovalResult>(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            results.Add(await RemoveOneAsync(service, item, cancellationToken));
        }

        return results;
    }

    private static async Task<RemovalResult> RemoveOneAsync(
        YouTubeService service,
        QueueItem item,
        CancellationToken cancellationToken
    )
    {
        // A track crateside is working that the playlist no longer holds has nothing to remove.
        if (item.PlaylistItemId is not { Length: > 0 } id)
            return new RemovalResult
            {
                PlaylistItemId = string.Empty,
                Title = item.Title,
                Removed = false,
                Reason = "It is not in the playlist.",
            };

        try
        {
            await service.PlaylistItems.Delete(id).ExecuteAsync(cancellationToken);

            return new RemovalResult
            {
                PlaylistItemId = id,
                Title = item.Title,
                Removed = true,
            };
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone. Idempotent by design.
            return new RemovalResult
            {
                PlaylistItemId = id,
                Title = item.Title,
                Removed = true,
            };
        }
        catch (GoogleApiException e)
        {
            return new RemovalResult
            {
                PlaylistItemId = id,
                Title = item.Title,
                Removed = false,
                Reason = Describe(e),
                CanSucceedLater = CanSucceedLater(e),
            };
        }
    }

    /// <summary>A failure of the moment, rather than one that will fail the same way for ever.</summary>
    /// <remarks>
    /// A 403 for the wrong account or a missing scope is permanent: retrying it only spends fifty
    /// units to be told the same thing (#34).
    /// </remarks>
    internal static bool CanSucceedLater(GoogleApiException e) =>
        (int)e.HttpStatusCode >= 500
        || e.Error?.Errors?.FirstOrDefault()?.Reason
            is "quotaExceeded"
                or "rateLimitExceeded"
                or "backendError";

    /// <summary>
    /// Turns a Google API failure into something diagnosable. Every interesting way a delete
    /// can fail is a 403, so the <c>reason</c> code is what gets surfaced, not the HTTP status.
    /// </summary>
    private static string Describe(GoogleApiException e)
    {
        var reason = e.Error?.Errors?.FirstOrDefault()?.Reason;
        var message = e.Error?.Message ?? e.Message;

        return reason switch
        {
            null => message,

            // The token is valid but belongs to an account that does not own the playlist.
            "playlistItemsNotAccessible" or "playlistOperationUnsupported" or "forbidden" =>
                $"{reason}: the authorized account cannot modify this playlist. Check that "
                    + "consent was granted for the channel that owns it.",

            "insufficientPermissions" =>
                $"{reason}: the token was not granted the youtube scope. Use \"Forget access\" "
                    + "and grant again.",

            "quotaExceeded" or "rateLimitExceeded" =>
                $"{reason}: out of quota. Each delete costs 50 units of the daily 10,000.",

            _ => $"{reason}: {message}",
        };
    }
}

/// <summary>The outcome of removing one playlist item.</summary>
public sealed record RemovalResult
{
    public required string PlaylistItemId { get; init; }
    public required string Title { get; init; }
    public required bool Removed { get; init; }

    /// <summary>Why it was not removed. Null when it was.</summary>
    public string? Reason { get; init; }

    public bool CanSucceedLater { get; init; }
}
