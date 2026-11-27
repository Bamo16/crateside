using System.Text;
using Crateside.Core;

namespace Crateside.Tests;

public sealed class TrackKeysTests
{
    [Theory]
    [InlineData("vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("https://youtu.be/vfBJjufAG1E?t=42", "vfBJjufAG1E")]
    [InlineData("soundcloud~242136202", "soundcloud~242136202")]
    [InlineData("file~3fa9c0d12be84471", "file~3fa9c0d12be84471")]
    [InlineData("track:vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("track:soundcloud~242136202", "soundcloud~242136202")]
    [InlineData("  track:file~3fa9c0d12be84471 ", "file~3fa9c0d12be84471")]
    public void Reads_the_key_from_every_shape_a_key_travels_in(string input, string expected)
    {
        Assert.True(TrackKeys.TryParse(input, out var key));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("https://soundcloud.com/artist/track")]
    [InlineData("track:")]
    [InlineData("Soundcloud~1")]
    [InlineData("soundcloud~")]
    [InlineData("soundcloud~a/b")]
    public void Refuses_rather_than_guessing_a_key(string? input)
    {
        Assert.False(TrackKeys.TryParse(input, out var key));
        Assert.Null(key);
    }

    [Fact]
    public void A_key_that_came_from_provenance_parses_back_to_itself()
    {
        var key = TrackKeys.ForSite("Soundcloud", "242136202");

        Assert.Equal("soundcloud~242136202", key);
        Assert.True(TrackKeys.TryParse(TrackKeys.ProvenanceOf(key), out var parsed));
        Assert.Equal(key, parsed);
    }

    [Fact]
    public void A_site_id_with_unsafe_characters_still_makes_a_key_that_parses()
    {
        var key = TrackKeys.ForSite("Generic", "a b/c");

        Assert.True(TrackKeys.TryParse(key, out var parsed));
        Assert.Equal("generic~a-b-c", parsed);
    }

    [Fact]
    public void The_same_content_is_the_same_key_and_other_content_is_not()
    {
        using var first = new MemoryStream(Encoding.UTF8.GetBytes("one"));
        using var again = new MemoryStream(Encoding.UTF8.GetBytes("one"));
        using var other = new MemoryStream(Encoding.UTF8.GetBytes("two"));

        var key = TrackKeys.ForFileContent(first);

        Assert.Equal(key, TrackKeys.ForFileContent(again));
        Assert.NotEqual(key, TrackKeys.ForFileContent(other));
        Assert.Matches("^file~[0-9a-f]{16}$", key);
    }

    [Theory]
    [InlineData("vfBJjufAG1E", true)]
    [InlineData("soundcloud~1", false)]
    [InlineData("file~3fa9c0d12be84471", false)]
    [InlineData(null, false)]
    public void Only_a_bare_key_is_a_video_id(string? key, bool expected) =>
        Assert.Equal(expected, TrackKeys.IsVideoId(key));
}
