using Crateside.Core.Ledger;
using Crateside.Core.Pairing;

namespace Crateside.Tests.Pairing;

public sealed class MashupPartnerTests
{
    private static readonly TonalCenter CMajor = new("C", DiatonicMode.Ionian);
    private static readonly TonalCenter GMajor = new("G", DiatonicMode.Ionian);

    [Theory]
    [InlineData(3, "+3")]
    [InlineData(0, "0")]
    [InlineData(-3, "−3")]
    public void A_shift_is_shown_with_a_plus_or_a_real_minus(int shift, string expected)
    {
        Assert.Equal(expected, MashupPartner.FormatShift(shift));
    }

    [Fact]
    public void A_partner_takes_the_shift_nearest_zero_and_lists_the_others_nearest_first()
    {
        Assert.True(MashupPartner.TryCreate(CMajor, GMajor, -6, 6, out var partner));

        Assert.Equal(0, partner.Shift);
        Assert.Equal([-2, 5], partner.AltShifts);
        Assert.Equal("−2, +5", partner.AltShiftsDisplay);
        Assert.Equal(6, partner.NotesShared);
        Assert.Null(partner.Relationship);
    }

    [Fact]
    public void A_narrow_range_forces_the_listed_shift_and_the_notes_are_counted_after_the_move()
    {
        Assert.True(MashupPartner.TryCreate(CMajor, GMajor, 3, 5, out var partner));

        Assert.Equal(5, partner.Shift);
        Assert.Equal(5, partner.AbsoluteShift);
        Assert.Equal([0, -2], partner.AltShifts);
        // G major moved up five is C major: all seven of the source's notes.
        Assert.Equal(7, partner.NotesShared);
        Assert.Null(partner.Relationship);
    }

    [Fact]
    public void A_range_holding_none_of_the_partners_shifts_gives_no_partner()
    {
        Assert.False(MashupPartner.TryCreate(CMajor, GMajor, 1, 1, out var partner));
        Assert.Null(partner);
    }

    [Fact]
    public void The_source_itself_is_named_the_same_key_at_shift_zero()
    {
        Assert.True(MashupPartner.TryCreate(CMajor, CMajor, -6, 6, out var partner));

        Assert.Equal(Relationship.SameKey, partner.Relationship);
        Assert.Equal(0, partner.Shift);
        Assert.Equal(7, partner.NotesShared);
    }

    [Fact]
    public void The_relative_key_is_named_same_notes_at_shift_zero()
    {
        Assert.True(
            MashupPartner.TryCreate(
                CMajor,
                new TonalCenter("A", DiatonicMode.Aeolian),
                -6,
                6,
                out var partner
            )
        );

        Assert.Equal(Relationship.SameNotes, partner.Relationship);
        Assert.Equal(0, partner.Shift);
    }

    [Fact]
    public void A_modal_sources_collapse_is_flagged_as_the_tag_and_needs_no_shift()
    {
        var dorian = new TonalCenter("A", DiatonicMode.Dorian);

        Assert.True(
            MashupPartner.TryCreate(
                dorian,
                new TonalCenter("A", DiatonicMode.Aeolian),
                -6,
                6,
                out var partner
            )
        );

        Assert.True(partner.IsTag);
        Assert.Equal(0, partner.Shift);
    }
}
