using Crateside.Core.Ableton;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Ableton;

public sealed class CamelotTagsTests
{
    [Theory]
    [InlineData("A", DiatonicMode.Aeolian, new[] { "Camelot|8", "Camelot|A" })]
    [InlineData("C", DiatonicMode.Ionian, new[] { "Camelot|8", "Camelot|B" })]
    [InlineData("G", DiatonicMode.Ionian, new[] { "Camelot|9", "Camelot|B" })]
    [InlineData("F", DiatonicMode.Ionian, new[] { "Camelot|7", "Camelot|B" })]
    [InlineData("C#", DiatonicMode.Aeolian, new[] { "Camelot|12", "Camelot|A" })]
    [InlineData("D#", DiatonicMode.Aeolian, new[] { "Camelot|2", "Camelot|A" })]
    [InlineData("F#", DiatonicMode.Ionian, new[] { "Camelot|2", "Camelot|B" })]
    [InlineData("D", DiatonicMode.Aeolian, new[] { "Camelot|7", "Camelot|A" })]
    public void A_major_or_minor_key_gets_its_number_and_letter(
        string tonic,
        DiatonicMode mode,
        string[] expected
    ) => Assert.Equal(expected, CamelotTags.For([new TonalCenter(tonic, mode)]));

    [Theory]
    [InlineData("D", DiatonicMode.Dorian)]
    [InlineData("G", DiatonicMode.Mixolydian)]
    [InlineData("E", DiatonicMode.Phrygian)]
    [InlineData("F", DiatonicMode.Lydian)]
    [InlineData("B", DiatonicMode.Locrian)]
    public void A_modal_center_gets_the_number_of_its_note_set_and_no_letter(
        string tonic,
        DiatonicMode mode
    ) => Assert.Equal(["Camelot|8"], CamelotTags.For([new TonalCenter(tonic, mode)]));

    [Fact]
    public void A_second_centre_adds_its_own_tags_to_the_first()
    {
        var tags = CamelotTags.For([
            new TonalCenter("A", DiatonicMode.Aeolian),
            new TonalCenter("B", DiatonicMode.Aeolian),
        ]);

        Assert.Equal(["Camelot|8", "Camelot|10", "Camelot|A"], tags);
    }

    [Fact]
    public void Two_centres_in_the_same_note_set_share_a_number()
    {
        var tags = CamelotTags.For([
            new TonalCenter("A", DiatonicMode.Aeolian),
            new TonalCenter("C", DiatonicMode.Ionian),
        ]);

        Assert.Equal(["Camelot|8", "Camelot|A", "Camelot|B"], tags);
    }

    [Fact]
    public void A_stray_camelot_tag_is_replaced_from_the_key()
    {
        var bag = CamelotTags.Reconcile([
            "Key|A",
            "Key|Minor",
            "Camelot|3",
            "Camelot|B",
            "Type|Vocal",
        ]);

        Assert.Equal(["Key|A", "Key|Minor", "Type|Vocal", "Camelot|8", "Camelot|A"], bag);
    }

    [Fact]
    public void A_modal_tag_beside_the_collapse_drops_the_letter()
    {
        var bag = CamelotTags.Reconcile([
            "Key|D",
            "Key|Minor",
            "Key|Dorian",
            "Camelot|7",
            "Camelot|A",
        ]);

        Assert.Equal(["Key|D", "Key|Minor", "Key|Dorian", "Camelot|8"], bag);
    }

    [Fact]
    public void A_flat_or_sharp_tonic_in_lives_spelling_is_read()
    {
        Assert.Equal(
            ["Key|G♯", "Key|Major", "Camelot|4", "Camelot|B"],
            CamelotTags.Reconcile(["Key|G♯", "Key|Major"])
        );
    }

    [Fact]
    public void A_bag_with_no_whole_key_is_left_as_it_is()
    {
        string[] bag = ["Key|D", "Type|Vocal", "Camelot|1"];

        Assert.Equal(bag, CamelotTags.Reconcile(bag));
    }

    [Theory]
    [InlineData("Camelot|1", true)]
    [InlineData("Camelot|12", true)]
    [InlineData("Camelot|A", true)]
    [InlineData("Camelot|B", true)]
    [InlineData("Camelot|0", false)]
    [InlineData("Camelot|13", false)]
    [InlineData("Camelot|C", false)]
    public void The_writer_knows_exactly_the_fourteen_tags(string keyword, bool known) =>
        Assert.Equal(known, CratesideKeywords.IsKnown(keyword));
}
