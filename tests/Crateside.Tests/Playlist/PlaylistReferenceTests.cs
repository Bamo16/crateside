using Crateside.Core.Playlist;

namespace Crateside.Tests.Playlist;

public sealed class PlaylistReferenceTests
{
    [Theory]
    [InlineData("PLrAXtmRdnEQy6nuLMHjMZOz59Oq8B9Ck1", "PLrAXtmRdnEQy6nuLMHjMZOz59Oq8B9Ck1")]
    [InlineData(
        "https://www.youtube.com/playlist?list=PLrAXtmRdnEQy6nuLMHjMZOz59Oq8B9Ck1",
        "PLrAXtmRdnEQy6nuLMHjMZOz59Oq8B9Ck1"
    )]
    // The address bar usually has a video ID in front of the list — take the list.
    [InlineData(
        "https://www.youtube.com/watch?v=vfBJjufAG1E&list=PLrAXtmRdnEQy6nuLMHjMZOz59Oq8B9Ck1&index=2",
        "PLrAXtmRdnEQy6nuLMHjMZOz59Oq8B9Ck1"
    )]
    [InlineData("https://music.youtube.com/playlist?list=PL123456", "PL123456")]
    [InlineData("  PL123456  ", "PL123456")]
    // Percent-encoded values are decoded for us.
    [InlineData("https://www.youtube.com/playlist?list=PL%5F123", "PL_123")]
    // A repeated key takes the first usable value, rather than the indexer's joined "A,B".
    [InlineData("https://www.youtube.com/playlist?list=PL123&list=PL456", "PL123")]
    // ...and skips a malformed one rather than failing on it.
    [InlineData("https://www.youtube.com/playlist?list=has%20a%20space&list=PL456", "PL456")]
    public void Takes_an_id_or_a_pasted_url(string input, string expected)
    {
        Assert.True(PlaylistReference.TryParse(input, out var playlistId));
        Assert.Equal(expected, playlistId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a playlist")]
    [InlineData("https://www.youtube.com/watch?v=vfBJjufAG1E")] // a video, with no list
    [InlineData("https://www.youtube.com/playlist?list")] // the key with no value
    [InlineData("https://www.youtube.com/playlist?list=")] // an empty value
    [InlineData("https://www.youtube.com/playlist?list=has%20a%20space")]
    public void Fails_when_there_is_no_playlist_in_the_input(string? input)
    {
        Assert.False(PlaylistReference.TryParse(input, out var playlistId));
        Assert.Null(playlistId);
    }

    [Fact]
    public void Watch_later_is_rejected_with_an_explanation()
    {
        // It parses as well-formed, so without this it would fail later as a bare "not
        // found" — which reads as a typo rather than as an impossibility.
        var problem = PlaylistReference.Problem("WL");

        Assert.NotNull(problem);
        Assert.Contains("Watch Later", problem);
    }

    [Theory]
    [InlineData("PLrAXtmRdnEQy6nuLMHjMZOz59Oq8B9Ck1")]
    [InlineData("LL")]
    public void A_usable_id_reports_no_problem(string id) =>
        Assert.Null(PlaylistReference.Problem(id));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("has spaces")]
    public void An_unusable_id_reports_a_problem(string? id) =>
        Assert.NotNull(PlaylistReference.Problem(id));
}
