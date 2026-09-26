using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>Completeness is asked of samples, not of files (ADR 0004).</summary>
public class StagedSongCompletenessTests
{
    private static StagedFile Stem(string? preset, bool tagged = false) =>
        new()
        {
            Path = $@"P:\staging\song ({preset ?? "source"}).flac",
            FileName = $"song ({preset ?? "source"}).flac",
            Preset = preset,
            AbletonKey = tagged ? "D minor" : null,
        };

    private static StagedSong Song(params StagedFile[] stems) =>
        new() { DisplayName = "song", Stems = stems };

    [Theory]
    [InlineData("Drums", true)]
    [InlineData("drums", true)] // the preset string is StemForge's, so do not depend on its case
    [InlineData("Vocal - Full", false)]
    [InlineData("Instrumental - Full", false)]
    [InlineData(null, false)] // a verbatim source download carries no preset
    public void A_drums_stem_is_the_warp_guide(string? preset, bool expected) =>
        Assert.Equal(expected, Stem(preset).IsWarpGuide);

    [Fact]
    public void A_song_is_finished_once_its_samples_are_tagged_even_with_a_guide_still_present()
    {
        // Both keepers tagged, warping still to do.
        var song = Song(
            Stem("Vocal - Full", tagged: true),
            Stem("Instrumental - Full", tagged: true),
            Stem("Drums")
        );

        Assert.True(song.IsFullyTagged);
        Assert.Equal(2, song.Samples.Count);
        Assert.Single(song.WarpGuides);
    }

    [Fact]
    public void An_untagged_sample_still_holds_the_song_open()
    {
        var song = Song(Stem("Vocal - Full", tagged: true), Stem("Instrumental - Full"));

        Assert.False(song.IsFullyTagged);
    }

    [Fact]
    public void A_song_of_nothing_but_a_warp_guide_is_not_finished()
    {
        // "Every sample is tagged" is vacuously true of no samples.
        Assert.False(Song(Stem("Drums")).IsFullyTagged);
    }

    [Fact]
    public void An_instrumental_release_keeps_its_source_as_the_sample()
    {
        // An instrumental release: the source is kept and the drums are only the guide.
        var song = Song(Stem(preset: null, tagged: true), Stem("Drums"));

        Assert.True(song.IsFullyTagged);
        Assert.Equal("source", song.Samples.Single().Label);
    }
}
