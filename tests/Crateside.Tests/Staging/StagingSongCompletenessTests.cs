using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// Completeness is asked of <em>samples</em>, not of files. A warp guide is a stem made to be
/// looked at while warping and then deleted, so counting it made "is this song finished?"
/// unanswerable — see docs/adr/0004.
/// </summary>
public class StagingSongCompletenessTests
{
    private static StagingStem Stem(string? preset, bool tagged = false) =>
        new()
        {
            Path = $@"P:\staging\song ({preset ?? "source"}).flac",
            FileName = $"song ({preset ?? "source"}).flac",
            Preset = preset,
            AbletonKey = tagged ? "D minor" : null,
        };

    private static StagingSong Song(params StagingStem[] stems) =>
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
        // The case that was previously unreachable: both keepers tagged, warping still to do.
        // Counting the guide left this at "one stem untagged", indistinguishable from having
        // forgotten to tag something.
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
        // Guarding a real edge of the rule rather than a hypothetical: "every sample is tagged"
        // is vacuously true of no samples, which would report a song with nothing kept as done.
        Assert.False(Song(Stem("Drums")).IsFullyTagged);
    }

    [Fact]
    public void An_instrumental_release_keeps_its_source_as_the_sample()
    {
        // Snail's House and similar: nothing to separate out, so the drums are the only stem
        // and the source track itself is what gets warped, tagged and kept.
        var song = Song(Stem(preset: null, tagged: true), Stem("Drums"));

        Assert.True(song.IsFullyTagged);
        Assert.Equal("source", song.Samples.Single().Label);
    }
}
