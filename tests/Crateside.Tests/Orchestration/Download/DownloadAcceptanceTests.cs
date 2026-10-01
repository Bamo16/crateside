using Crateside.Core.Orchestration.Download;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Orchestration.Download;

/// <summary>An acceptance outlives the working set, and covers only the upload it was given for (#31).</summary>
public sealed class DownloadAcceptanceTests
{
    private const string Track = "Dbhzw8xyc7A";
    private const string Better = "9Zo8jqTqju0";

    private static WantedDownload Wanted(string url, string? accepted, string? label = null) =>
        new()
        {
            VideoId = Track,
            Url = url,
            Label = label,
            Track = new QueueItem
            {
                VideoId = Track,
                Title = "Track",
                AcceptedUpload = accepted,
            },
        };

    [Fact]
    public void A_fresh_download_of_the_accepted_upload_arrives_accepted()
    {
        Assert.True(
            DownloadDrainer.IsAccepted(Wanted($"https://www.youtube.com/watch?v={Track}", Track))
        );
    }

    [Fact]
    public void Nothing_accepted_is_nothing_accepted()
    {
        Assert.False(
            DownloadDrainer.IsAccepted(Wanted($"https://www.youtube.com/watch?v={Track}", null))
        );
    }

    [Fact]
    public void Swapping_to_another_upload_asks_again()
    {
        Assert.False(
            DownloadDrainer.IsAccepted(Wanted($"https://music.youtube.com/watch?v={Better}", Track))
        );
    }

    [Fact]
    public void An_added_source_is_never_the_track_being_accepted()
    {
        Assert.False(
            DownloadDrainer.IsAccepted(
                Wanted($"https://www.youtube.com/watch?v={Track}", Track, label: "Acapella")
            )
        );
    }
}
