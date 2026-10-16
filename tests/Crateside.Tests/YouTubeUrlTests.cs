using Crateside.Core;

namespace Crateside.Tests;

public sealed class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=vfBJjufAG1E")]
    [InlineData("https://youtube.com/watch?v=vfBJjufAG1E&list=PL123&index=4")]
    [InlineData("https://youtu.be/vfBJjufAG1E")]
    [InlineData("https://youtu.be/vfBJjufAG1E?t=42")]
    [InlineData("https://www.youtube.com/shorts/vfBJjufAG1E")]
    [InlineData("https://www.youtube.com/embed/vfBJjufAG1E")]
    // StemForge's pattern also accepts music/m subdomains, a missing scheme, and a bare ID.
    [InlineData("https://music.youtube.com/watch?v=vfBJjufAG1E")]
    [InlineData("https://m.youtube.com/watch?v=vfBJjufAG1E")]
    [InlineData("www.youtube.com/watch?v=vfBJjufAG1E")]
    [InlineData("youtu.be/vfBJjufAG1E")]
    [InlineData("vfBJjufAG1E")]
    public void Reads_the_video_id_from_every_youtube_spelling(string input)
    {
        Assert.True(YouTubeUrl.TryGetVideoId(input, out var videoId));
        Assert.Equal("vfBJjufAG1E", videoId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("https://soundcloud.com/artist/track")]
    [InlineData("https://www.youtube.com/watch?v=tooshort")]
    [InlineData("https://www.youtube.com/watch?list=PL123")]
    public void Refuses_rather_than_guessing_a_video_id(string? input)
    {
        Assert.False(YouTubeUrl.TryGetVideoId(input, out var videoId));
        Assert.Null(videoId);
    }

    [Theory]
    [InlineData("vfBJjufAG1E")]
    [InlineData("https://youtu.be/vfBJjufAG1E")]
    [InlineData("https://www.youtube.com/watch?v=vfBJjufAG1E")]
    public void A_youtube_reference_normalises_to_one_canonical_watch_url(string input)
    {
        // The ledger refuses anything that is not an http(s) URL, so a bare ID has to become one
        // before it leaves crateside.
        Assert.True(YouTubeUrl.TryNormalize(input, out var normalized));
        Assert.Equal("https://music.youtube.com/watch?v=vfBJjufAG1E", normalized);
    }

    [Fact]
    public void A_non_youtube_http_url_is_left_alone()
    {
        // yt-dlp reaches well past YouTube, so a URL crateside cannot read an ID out of is not
        // therefore a bad URL.
        Assert.True(YouTubeUrl.TryNormalize("https://soundcloud.com/artist/track", out var url));
        Assert.Equal("https://soundcloud.com/artist/track", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/track.flac")]
    public void Anything_that_is_not_a_reachable_url_is_refused(string? input) =>
        Assert.False(YouTubeUrl.TryNormalize(input, out _));
}
