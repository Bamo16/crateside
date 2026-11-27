using Crateside.Core.Ableton;

namespace Crateside.Tests.Ableton;

public sealed class TempoTagsTests
{
    [Theory]
    // DnB folds down an octave to sit beside hip hop.
    [InlineData(174, new[] { "Tempo|≈82 · 165", "Tempo|≈87 · 174" })]
    [InlineData(87, new[] { "Tempo|≈82 · 165", "Tempo|≈87 · 174" })]
    // A slow tempo folds up.
    [InlineData(60, new[] { "Tempo|≈116 · 233", "Tempo|≈123 · 247" })]
    [InlineData(120, new[] { "Tempo|≈116 · 233", "Tempo|≈123 · 247" })]
    [InlineData(100, new[] { "Tempo|≈98 · 196", "Tempo|≈104 · 207" })]
    [InlineData(198, new[] { "Tempo|≈98 · 196", "Tempo|≈104 · 207" })]
    public void A_tempo_folds_into_one_octave_and_takes_its_two_nearest_bands(
        double bpm,
        string[] expected
    ) => Assert.Equal(expected, TempoTags.For(bpm));

    [Fact]
    public void A_tempo_at_the_top_of_the_octave_wraps_to_the_bottom_band()
    {
        // 158 is nearest the 155 centre, then the next one up, which is the first band again.
        Assert.Equal(["Tempo|≈82 · 165", "Tempo|≈155 · 311"], TempoTags.For(158));
        Assert.Equal(TempoTags.For(158), TempoTags.For(79));
    }

    [Fact]
    public void Either_side_of_a_band_edge_a_file_keeps_one_tag_in_common()
    {
        // The pair changes as a tempo passes the 98 centre (97.9); the 98 tag spans the change.
        var below = TempoTags.For(97.8);
        var above = TempoTags.For(98.0);

        Assert.NotEqual(below, above);
        Assert.Single(below.Intersect(above));
    }

    [Fact]
    public void Every_tempo_gets_exactly_two_tags_the_writer_knows()
    {
        var tags = Enumerable
            .Range(300, 1500)
            .Select(tenths => TempoTags.For(tenths / 10.0))
            .ToList();

        Assert.All(tags, pair => Assert.Equal(2, pair.Distinct().Count()));
        Assert.All(
            tags.SelectMany(pair => pair),
            tag => Assert.True(CratesideKeywords.IsKnown(tag))
        );
        Assert.Equal(TempoTags.BandsPerOctave, tags.SelectMany(pair => pair).Distinct().Count());
    }

    [Fact]
    public void A_stray_tempo_tag_is_replaced_from_the_warp()
    {
        var bag = TempoTags.Reconcile(
            ["Key|A", "Key|Minor", "Tempo|≈155 · 311", "Type|Vocal"],
            Tempo(174)
        );

        Assert.Equal(
            ["Key|A", "Key|Minor", "Type|Vocal", "Tempo|≈82 · 165", "Tempo|≈87 · 174"],
            bag
        );
    }

    [Fact]
    public void A_file_with_no_warp_ends_with_no_tempo_tag()
    {
        Assert.Equal(
            ["Key|A", "Key|Minor"],
            TempoTags.Reconcile(["Key|A", "Key|Minor", "Tempo|≈87 · 174"], tempo: null)
        );
    }

    [Fact]
    public void A_bag_with_no_whole_key_is_left_as_it_is()
    {
        string[] bag = ["Key|D", "Tempo|≈87 · 174"];

        Assert.Equal(bag, TempoTags.Reconcile(bag, Tempo(120)));
    }

    [Theory]
    [InlineData("Tempo|≈87 · 174", true)]
    [InlineData("Tempo|≈155 · 311", true)]
    [InlineData("Tempo|≈87", false)]
    [InlineData("Tempo|174", false)]
    public void The_writer_knows_exactly_the_twelve_names(string keyword, bool known) =>
        Assert.Equal(known, CratesideKeywords.IsKnown(keyword));

    private static WarpTempo Tempo(double bpm) => new(bpm, TempoShape.Steady, bpm, bpm, []);
}
