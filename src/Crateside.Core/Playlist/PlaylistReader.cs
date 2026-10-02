using Crateside.Core.WorkLists;
using Google;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Playlist;

/// <summary>
/// Reads the Queue playlist over the YouTube Data API. An API key silently stops working if the
/// playlist goes private; OAuth does not.
/// </summary>
public sealed class PlaylistReader(IOptions<PlaylistOptions> options) : IDisposable
{
    private const int MaxPageSize = 50;

    private readonly PlaylistOptions _options = options.Value;
    private YouTubeService? _service;

    /// <summary>Whether a credential is available. The playlist ID is passed per call.</summary>
    public bool HasCredential => !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <summary>
    /// Lists every item in the playlist, following pagination to the end.
    /// </summary>
    /// <exception cref="PlaylistUnavailableException">
    /// Thrown rather than returning empty: a private playlist read with an API key comes back as
    /// <em>not found</em>, and an unreadable queue must never render as an empty one.
    /// </exception>
    public async Task<IReadOnlyList<WorkItem>> ListAsync(
        string playlistId,
        CancellationToken ct = default
    )
    {
        if (!HasCredential)
            throw new PlaylistUnavailableException("No API key is configured.");

        if (PlaylistReference.Problem(playlistId) is { } problem)
            throw new PlaylistUnavailableException(problem);

        var service = _service ??= new YouTubeService(
            new BaseClientService.Initializer
            {
                ApplicationName = "crateside",
                ApiKey = _options.ApiKey,
            }
        );

        var items = new List<WorkItem>();
        string? pageToken = null;

        try
        {
            do
            {
                var request = service.PlaylistItems.List("snippet,status");
                request.PlaylistId = playlistId;
                request.MaxResults = MaxPageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);

                items.AddRange(response.Items.Select(ToQueueItem).OfType<WorkItem>());

                pageToken = response.NextPageToken;
            } while (pageToken is not null);
        }
        catch (GoogleApiException ex)
        {
            throw new PlaylistUnavailableException(
                ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound
                    ? "The playlist was not found. If it has been made private, an API key can "
                        + "no longer read it and an OAuth credential is required."
                    : $"YouTube refused the request: {ex.Error?.Message ?? ex.Message}",
                ex
            );
        }

        return items;
    }

    private static WorkItem? ToQueueItem(PlaylistItem item)
    {
        if (item.Snippet?.ResourceId?.VideoId is not { Length: > 0 } videoId)
            return null;

        // YouTube keeps the entry and blanks the title when a video goes away.
        var privacy = item.Status?.PrivacyStatus;
        var unavailable =
            privacy is "private" or "privacyStatusUnspecified"
            || item.Snippet.Title is "Private video" or "Deleted video";

        return new WorkItem
        {
            PlaylistItemId = item.Id,
            VideoId = videoId,
            Title = item.Snippet.Title ?? videoId,
            ChannelTitle = item.Snippet.VideoOwnerChannelTitle,
            AddedAt = item.Snippet.PublishedAtDateTimeOffset,
            Position = (int?)item.Snippet.Position,
            IsUnavailable = unavailable,
            PremiumOutlook = PremiumOutlookReader.FromDescription(item.Snippet.Description),
        };
    }

    public void Dispose() => _service?.Dispose();
}

/// <summary>The Queue could not be read. Distinct from the Queue being empty.</summary>
public sealed class PlaylistUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
