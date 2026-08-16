using Google;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;

namespace Crateside.Core.Queue;

/// <summary>
/// Deletes items from the Queue playlist. The only part of crateside that writes anything,
/// anywhere.
/// </summary>
/// <remarks>
/// <c>playlistItems.delete</c> costs <strong>50 quota units</strong> per call — fifty times a
/// list — so a bulk removal of a large batch is worth being deliberate about against the
/// 10,000/day default.
/// </remarks>
public sealed class QueueRemover(QueueAuthorizer authorizer)
{
    /// <summary>
    /// Removes the given items. Each is attempted independently: one failure does not abandon
    /// the rest, and the caller gets a per-item account of what happened.
    /// </summary>
    /// <remarks>
    /// An item already gone from the playlist reports as removed rather than as an error —
    /// deletion has to be idempotent, because the whole point is that it can be re-run safely
    /// after a partial failure.
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
        try
        {
            await service.PlaylistItems.Delete(item.PlaylistItemId).ExecuteAsync(cancellationToken);

            return new RemovalResult
            {
                PlaylistItemId = item.PlaylistItemId,
                Title = item.Title,
                Removed = true,
            };
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone. Idempotent by design.
            return new RemovalResult
            {
                PlaylistItemId = item.PlaylistItemId,
                Title = item.Title,
                Removed = true,
            };
        }
        catch (GoogleApiException e)
        {
            return new RemovalResult
            {
                PlaylistItemId = item.PlaylistItemId,
                Title = item.Title,
                Removed = false,
                Reason = Describe(e),
            };
        }
    }

    /// <summary>
    /// Turns a Google API failure into something diagnosable. The HTTP status alone is
    /// useless here — every interesting way a delete can fail is a 403, and it is the
    /// <c>reason</c> code that says which, so that is what gets surfaced.
    /// </summary>
    private static string Describe(GoogleApiException e)
    {
        var reason = e.Error?.Errors?.FirstOrDefault()?.Reason;
        var message = e.Error?.Message ?? e.Message;

        return reason switch
        {
            null => message,

            // The token is valid but belongs to an account that does not own the playlist.
            // A Google account can own several YouTube channels, and consent grants whichever
            // one was picked at the consent screen.
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
}
