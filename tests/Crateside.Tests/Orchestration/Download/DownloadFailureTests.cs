using Crateside.Core.Orchestration.Download;

namespace Crateside.Tests.Orchestration.Download;

public class DownloadFailureTests
{
    [Theory]
    // Seen on oFf86LTzUsk, 2026-09-25.
    [InlineData(
        "ERROR: [youtube] oFf86LTzUsk: Video unavailable. This video is not available",
        "upload unavailable"
    )]
    [InlineData(
        "ERROR: [youtube] aaaaaaaaaaa: Private video. Sign in if you've been granted access to this video",
        "upload private"
    )]
    [InlineData(
        "ERROR: [youtube] aaaaaaaaaaa: Video unavailable. This video has been removed by the uploader",
        "upload removed"
    )]
    [InlineData(
        "ERROR: [youtube] aaaaaaaaaaa: Video unavailable. This video is no longer available because the YouTube account associated with this video has been terminated.",
        "upload removed"
    )]
    [InlineData(
        "ERROR: [youtube] aaaaaaaaaaa: Video unavailable. This video contains content from UMG, who has blocked it on copyright grounds",
        "blocked on copyright"
    )]
    [InlineData(
        "ERROR: [youtube] aaaaaaaaaaa: The uploader has not made this video available in your country",
        "blocked in this country"
    )]
    public void An_upload_youtube_will_never_serve_is_permanent(string error, string reason)
    {
        Assert.True(DownloadFailure.IsPermanent(error, out var said));
        Assert.Equal(reason, said);
    }

    [Theory]
    [InlineData("ERROR: [youtube] aaaaaaaaaaa: Unable to download webpage: HTTP Error 503")]
    [InlineData("ERROR: unable to download video data: <urlopen error timed out>")]
    [InlineData("the source could not be copied: The process cannot access the file")]
    [InlineData(null)]
    public void Anything_else_is_worth_trying_again(string? error) =>
        Assert.False(DownloadFailure.IsPermanent(error, out _));
}
