using Google;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// Reads the Queue playlist over the YouTube Data API.
/// </summary>
/// <remarks>
/// <para>
/// <c>playlistItems.list</c> costs <strong>1 quota unit</strong> per call against a
/// 10,000/day default and pages 50 at a time, so a refresh is a handful of units.
/// </para>
/// <para>
/// The playlist is unlisted today, so a long-lived API key can read it. Prefer an OAuth
/// credential when one is available: it works at every visibility, and the key silently stops
/// working the day the playlist goes private.
/// </para>
/// </remarks>
public sealed class YouTubeQueueReader(IOptions<QueueOptions> options) : IDisposable
{
    private const int MaxPageSize = 50;

    private readonly QueueOptions _options = options.Value;
    private YouTubeService? _service;

    /// <summary>
    /// Whether a credential is available. The playlist ID is passed per call, since the user
    /// can change it from the UI without restarting.
    /// </summary>
    public bool HasCredential => !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <summary>
    /// Lists every item in the playlist, following pagination to the end.
    /// </summary>
    /// <exception cref="QueueUnavailableException">
    /// The playlist could not be read. A private playlist read with an API key comes back as
    /// <em>not found</em> rather than as a permission error, so this is thrown rather than
    /// returning empty — an unreadable queue must never render as an empty one.
    /// </exception>
    public async Task<IReadOnlyList<QueueItem>> ListAsync(
        string playlistId,
        CancellationToken cancellationToken = default
    )
    {
        if (!HasCredential)
            throw new QueueUnavailableException("No API key is configured.");

        if (PlaylistReference.Problem(playlistId) is { } problem)
            throw new QueueUnavailableException(problem);

        var service = _service ??= new YouTubeService(
            new BaseClientService.Initializer
            {
                ApplicationName = "crateside",
                ApiKey = _options.ApiKey,
            }
        );

        var items = new List<QueueItem>();
        string? pageToken = null;

        try
        {
            do
            {
                var request = service.PlaylistItems.List("snippet,status");
                request.PlaylistId = playlistId;
                request.MaxResults = MaxPageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(cancellationToken);

                items.AddRange(response.Items.Select(ToQueueItem).OfType<QueueItem>());

                pageToken = response.NextPageToken;
            } while (pageToken is not null);
        }
        catch (GoogleApiException e)
        {
            throw new QueueUnavailableException(
                e.HttpStatusCode == System.Net.HttpStatusCode.NotFound
                    ? "The playlist was not found. If it has been made private, an API key can "
                        + "no longer read it and an OAuth credential is required."
                    : $"YouTube refused the request: {e.Error?.Message ?? e.Message}",
                e
            );
        }

        return items;
    }

    private static QueueItem? ToQueueItem(PlaylistItem item)
    {
        if (item.Snippet?.ResourceId?.VideoId is not { Length: > 0 } videoId)
            return null;

        // YouTube keeps the entry and blanks the title when a video goes away.
        var privacy = item.Status?.PrivacyStatus;
        var unavailable =
            privacy is "private" or "privacyStatusUnspecified"
            || item.Snippet.Title is "Private video" or "Deleted video";

        return new QueueItem
        {
            PlaylistItemId = item.Id,
            VideoId = videoId,
            Title = item.Snippet.Title ?? videoId,
            ChannelTitle = item.Snippet.VideoOwnerChannelTitle,
            AddedAt = item.Snippet.PublishedAtDateTimeOffset,
            Position = (int?)item.Snippet.Position,
            IsUnavailable = unavailable,
            // The description comes back on this same call, so the premium signal is free.
            PremiumOutlook = PremiumOutlookReader.FromDescription(item.Snippet.Description),
        };
    }

    public void Dispose() => _service?.Dispose();
}

/// <summary>The Queue could not be read. Distinct from the Queue being empty.</summary>
public sealed class QueueUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
