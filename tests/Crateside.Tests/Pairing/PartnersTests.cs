using Crateside.Core.Pairing;

namespace Crateside.Tests.Pairing;

public class PartnersTests
{
    private static MusicKey Key(string name) =>
        MusicKey.FromString(name) ?? throw new ArgumentException($"'{name}' is not a key.");

    [Fact]
    public void All_24_keys_are_reachable_by_default()
    {
        Assert.Equal(24, Partners.For(Key("D# major")).Count);
    }

    [Fact]
    public void The_widest_shift_is_the_least_that_reaches_every_key_from_every_key()
    {
        Assert.All(
            MusicKey.AllKeys,
            key => Assert.Equal(24, Partners.For(key, Partners.WidestShift).Count)
        );
        Assert.All(
            MusicKey.AllKeys,
            key => Assert.NotEqual(24, Partners.For(key, Partners.WidestShift - 1).Count)
        );
    }

    [Fact]
    public void The_reproduced_d_sharp_major_ordering()
    {
        var unshifted = Partners.For(Key("D# major"), maxShift: 0);

        Assert.Equal([0, 0.1, 10, 10, 20, 30, 40], unshifted.Select(p => p.Score));
        Assert.Equal(
            ["D♯ Major", "C Minor", "G♯ Major", "A♯ Major", "A♯ Minor", "D♯ Minor", "G♯ Minor"],
            unshifted.Select(p => p.Key.Name)
        );
    }

    [Fact]
    public void Shift_outranks_circle_distance()
    {
        var partners = Partners.For(Key("C major"));

        var shifts = partners.Select(p => p.AbsoluteShift).ToArray();
        Assert.Equal(shifts.Order(), shifts);
    }

    [Fact]
    public void A_narrowed_max_shift_never_drops_the_source_key_itself()
    {
        foreach (var key in MusicKey.AllKeys)
            Assert.Contains(Partners.For(key, maxShift: 0), p => p.Key.Equals(key));
    }

    [Fact]
    public void The_exact_match_scores_zero_and_the_relative_scores_a_tenth()
    {
        var partners = Partners.For(Key("C major"));

        Assert.Equal(0, partners[0].Score);
        Assert.Equal(Relationship.PerfectMatch, partners[0].Relationship);

        Assert.Equal(0.1, partners[1].Score);
        Assert.Equal(Relationship.Relative, partners[1].Relationship);
        Assert.Equal("A Minor", partners[1].Key.Name);
    }

    [Fact]
    public void Alt_shifts_are_a_fifth_either_way_and_exclude_the_primary()
    {
        foreach (var source in MusicKey.AllKeys)
        {
            foreach (var partner in Partners.For(source))
            {
                Assert.DoesNotContain(partner.Shift, partner.AltShifts);
                Assert.All(partner.AltShifts, alt => Assert.InRange(alt, -6, 6));
                Assert.All(
                    partner.AltShifts,
                    alt => Assert.True(Math.Abs(alt) >= partner.AbsoluteShift)
                );
            }
        }
    }

    [Fact]
    public void A_negative_max_shift_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Partners.For(Key("C major"), -1));
}

public class MusicKeyTests
{
    [Theory]
    [InlineData("G# major", "G♯ Major")]
    [InlineData("g# major", "G♯ Major")]
    [InlineData("G♯ Major", "G♯ Major")]
    [InlineData("Ab major", "G♯ Major")] // enharmonic: flats fold to sharps
    [InlineData("A♭ Major", "G♯ Major")]
    [InlineData("C", "C Major")] // bare note defaults to major
    [InlineData("Am", "A Minor")]
    [InlineData("a min", "A Minor")]
    [InlineData("8B", "C Major")] // Camelot code
    [InlineData("5A", "C Minor")]
    public void Parses_every_spelling_of_a_key(string input, string expected) =>
        Assert.Equal(expected, MusicKey.FromString(input)?.Name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("H major")]
    [InlineData("13B")]
    [InlineData("not a key")]
    public void Returns_null_rather_than_guessing(string? input) =>
        Assert.Null(MusicKey.FromString(input));

    [Fact]
    public void The_ledger_spelling_round_trips()
    {
        foreach (var key in MusicKey.AllKeys)
            Assert.Equal(key, MusicKey.FromString(key.LedgerName));
    }

    [Fact]
    public void There_are_24_distinct_keys_with_24_distinct_camelot_codes()
    {
        Assert.Equal(24, MusicKey.AllKeys.Length);
        Assert.Equal(24, MusicKey.AllKeys.Distinct().Count());
        Assert.Equal(24, MusicKey.AllKeys.Select(k => k.CamelotCode).Distinct().Count());
    }

    [Fact]
    public void Enharmonic_keys_are_equal()
    {
        Assert.Equal(MusicKey.FromString("D# minor"), MusicKey.FromString("Eb minor"));
        Assert.Equal(
            MusicKey.FromString("D# minor")?.GetHashCode(),
            MusicKey.FromString("Eb minor")?.GetHashCode()
        );
    }

    [Theory]
    [InlineData("A# minor", "C# major")]
    [InlineData("C major", "A minor")]
    [InlineData("B minor", "D major")]
    public void The_relative_is_the_same_notes_in_the_other_mode(string key, string relative) =>
        Assert.Equal(MusicKey.FromString(relative), MusicKey.FromString(key)!.Relative);

    [Theory]
    [InlineData("A# minor", "A♯m")]
    [InlineData("C# major", "D♭")]
    [InlineData("B major", "B")]
    public void The_wheel_names_majors_flat_and_minors_sharp(string key, string shortName) =>
        Assert.Equal(shortName, MusicKey.FromString(key)!.ShortName);
}
