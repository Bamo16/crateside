using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

public class StemProvenanceTests
{
    // The exact comment read off "Alan Gomez - Mission 15 (Vocal - Full).flac".
    private const string RealComment =
        "stemforge/0.2.1.1 | preset: Vocal - Full | date: 2026-08-05 | "
        + "source: https://www.youtube.com/watch?v=vfBJjufAG1E | codec: mp4a.40.2 | "
        + "bitrate: 257.6 kbps | format-id: 141";

    [Fact]
    public void Parses_a_real_stemforge_comment()
    {
        var provenance = StemProvenance.Parse(RealComment);

        Assert.NotNull(provenance);
        Assert.Equal("stemforge/0.2.1.1", provenance.ToolVersion);
        Assert.Equal("Vocal - Full", provenance.Preset);
        Assert.Equal(new DateOnly(2026, 8, 5), provenance.SeparatedOn);
        Assert.Equal("https://www.youtube.com/watch?v=vfBJjufAG1E", provenance.SourceUrl);
        Assert.Equal("vfBJjufAG1E", provenance.SourceVideoId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ripped from vinyl, 2004")]
    [InlineData("STEMFORGE/0.2.1.1 | source: https://www.youtube.com/watch?v=vfBJjufAG1E")]
    public void Rejects_anything_stemforge_did_not_write(string? comment)
    {
        // The prefix gate is load-bearing: a foreign comment must read as "no video ID"
        // rather than as a partial parse that joins to nothing.
        Assert.Null(StemProvenance.Parse(comment));
    }

    [Fact]
    public void Degrades_to_the_prefix_when_a_local_file_job_carries_no_source()
    {
        var provenance = StemProvenance.Parse(
            "stemforge/0.3.1 | preset: Drums | date: 2026-08-05"
        );

        Assert.NotNull(provenance);
        Assert.Equal("Drums", provenance.Preset);
        Assert.Null(provenance.SourceUrl);
        Assert.Null(provenance.SourceVideoId);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("https://youtube.com/watch?v=vfBJjufAG1E&list=PL123&index=4", "vfBJjufAG1E")]
    [InlineData("https://youtu.be/vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("https://youtu.be/vfBJjufAG1E?t=42", "vfBJjufAG1E")]
    [InlineData("https://www.youtube.com/shorts/vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("https://www.youtube.com/embed/vfBJjufAG1E", "vfBJjufAG1E")]
    // StemForge's pattern also accepts music/m subdomains, a missing scheme, and a bare ID.
    [InlineData("https://music.youtube.com/watch?v=vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("https://m.youtube.com/watch?v=vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("www.youtube.com/watch?v=vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("youtu.be/vfBJjufAG1E", "vfBJjufAG1E")]
    [InlineData("vfBJjufAG1E", "vfBJjufAG1E")]
    public void Extracts_the_video_id_from_every_youtube_spelling(string url, string expected) =>
        Assert.Equal(expected, StemProvenance.ExtractYouTubeVideoId(url));

    [Theory]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("https://soundcloud.com/artist/track")]
    [InlineData("https://www.youtube.com/watch?v=tooshort")]
    [InlineData("https://www.youtube.com/watch?list=PL123")]
    public void Returns_null_rather_than_guessing_a_video_id(string? url) =>
        Assert.Null(StemProvenance.ExtractYouTubeVideoId(url));
}
