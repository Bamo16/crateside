using Crateside.Core.Imports;
using Crateside.Core.Samples;

namespace Crateside.Tests.Samples;

public sealed class StemProvenanceTests
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
        Assert.Equal("vfBJjufAG1E", provenance.SourceTrackKey);
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
        var provenance = StemProvenance.Parse("stemforge/0.3.1 | preset: Drums | date: 2026-08-05");

        Assert.NotNull(provenance);
        Assert.Equal("Drums", provenance.Preset);
        Assert.Null(provenance.SourceUrl);
        Assert.Null(provenance.SourceTrackKey);
    }

    [Theory]
    [InlineData("141", true)]
    [InlineData("774", true)]
    [InlineData("140", false)]
    [InlineData("251", false)]
    public void Only_the_formats_a_free_account_is_refused_count_as_premium(
        string formatId,
        bool premium
    )
    {
        var provenance = StemProvenance.Parse(
            $"stemforge/0.3.1.0 | date: 2026-09-18 | format-id: {formatId}"
        );

        Assert.Equal(formatId, provenance?.FormatId);
        Assert.Equal(premium, provenance?.IsPremium);
    }

    [Fact]
    public void A_file_that_recorded_no_format_says_nothing_about_premium()
    {
        Assert.Null(
            StemProvenance.Parse("stemforge/0.1.0.0 | model: x | date: 2026-05-29")?.IsPremium
        );
    }

    [Fact]
    public void A_kept_copy_of_a_track_not_on_youtube_reads_as_that_track_and_is_not_adopted()
    {
        var provenance = StemProvenance.Parse(
            KeptSourceStamp.Comment("file~3fa9c0d12be84471", new DateOnly(2026, 11, 4))
        );

        Assert.Equal("file~3fa9c0d12be84471", provenance?.SourceTrackKey);
        Assert.Equal("track:file~3fa9c0d12be84471", provenance?.SourceUrl);
        Assert.False(provenance?.IsAdopted);
    }

    [Fact]
    public void A_labelled_crateside_comment_is_an_adopted_sample()
    {
        Assert.True(
            StemProvenance
                .Parse(
                    "crateside/1.0 | label: Mix | source: https://www.youtube.com/watch?v=vfBJjufAG1E"
                )
                ?.IsAdopted
        );
    }

    [Fact]
    public void Stamping_a_copy_leaves_the_original_comment_where_it_was()
    {
        var original = Path.Combine(Path.GetTempPath(), $"crateside-tests-{Guid.NewGuid():n}.flac");
        var copy = Path.Combine(Path.GetTempPath(), $"crateside-tests-{Guid.NewGuid():n}.flac");

        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), original);

            using (var file = TagLib.File.Create(original))
            {
                file.Tag.Comment = "ripped by someone";
                file.Save();
            }

            File.Copy(original, copy);

            Assert.True(
                KeptSourceStamp.TryWrite(copy, "file~aa", new DateOnly(2026, 11, 4), out _)
            );

            using var stamped = TagLib.File.Create(copy);
            using var untouched = TagLib.File.Create(original);

            Assert.Equal("file~aa", StemProvenance.Parse(stamped.Tag.Comment)?.SourceTrackKey);
            Assert.Equal("ripped by someone", untouched.Tag.Comment);
        }
        finally
        {
            File.Delete(original);
            File.Delete(copy);
        }
    }
}
