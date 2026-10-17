using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Pairing;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Pairing;

public sealed class PartnersTests
{
    public static TheoryData<DiatonicMode> Modes => [.. Enum.GetValues<DiatonicMode>()];

    [Fact]
    public void A_plain_key_lists_brief_19s_rows_at_shift_0()
    {
        var rows = Partners.For(Centre("C", DiatonicMode.Ionian));

        (string, int, Relationship?)[] expected =
        [
            ("C major", 7, Relationship.SameKey),
            ("A minor", 7, Relationship.SameNotes),
            ("G major", 6, null),
            ("F major", 6, null),
            ("G minor", 5, null),
            ("C minor", 4, null),
            ("F minor", 3, null),
        ];

        Assert.Equal(
            expected,
            rows.Select(row => (row.Key.Name, row.NotesShared, row.Relationship))
        );
    }

    [Fact]
    public void A_dorian_lists_brief_19s_rows_at_shift_0()
    {
        var rows = Partners.For(Centre("A", DiatonicMode.Dorian));

        (string, int, Relationship?)[] expected =
        [
            ("A Dorian", 7, Relationship.SameKey),
            ("G major", 7, Relationship.SameNotes),
            ("E minor", 7, Relationship.SameNotes),
            ("A minor", 6, null),
            ("C major", 6, null),
            ("D major", 6, null),
            ("D minor", 5, null),
            ("A major", 5, null),
            ("E major", 4, null),
        ];

        Assert.Equal(
            expected,
            rows.Select(row => (row.Key.Name, row.NotesShared, row.Relationship))
        );
        Assert.Equal(["A minor"], rows.Where(row => row.IsTag).Select(row => row.Key.Name));
    }

    [Theory]
    [InlineData(DiatonicMode.Ionian, "F# minor")]
    [InlineData(DiatonicMode.Dorian, "G major", "E minor")]
    [InlineData(DiatonicMode.Phrygian, "F major", "D minor")]
    [InlineData(DiatonicMode.Lydian, "E major", "C# minor")]
    [InlineData(DiatonicMode.Mixolydian, "D major", "B minor")]
    [InlineData(DiatonicMode.Aeolian, "C major")]
    [InlineData(DiatonicMode.Locrian, "A# major", "G minor")]
    public void Every_same_notes_key_is_at_shift_0_and_labelled(
        DiatonicMode mode,
        params string[] sameNotes
    ) =>
        Assert.Equal(
            sameNotes,
            Partners
                .For(Centre("A", mode))
                .Where(row => row.Relationship is Relationship.SameNotes)
                .Select(row => row.Key.Name)
        );

    [Theory]
    [MemberData(nameof(Modes))]
    public void A_source_is_its_own_first_row(DiatonicMode mode)
    {
        var source = Centre("A", mode);

        Assert.Equal(
            (source, 0, 7, Relationship.SameKey),
            Partners.For(source)[0] is var first
                ? (first.Key, first.Shift, first.NotesShared, first.Relationship)
                : default
        );
    }

    [Fact]
    public void A_plain_key_keeps_the_shift_rule_on_all_24_keys()
    {
        foreach (var source in MusicKey.AllKeys.Select(MusicKey.CentreOf))
        {
            foreach (
                var partner in Partners.For(source, -Partners.FarthestShift, Partners.FarthestShift)
            )
            {
                var landed = (partner.Key.Chromatic + partner.Shift - source.Chromatic + 12) % 12;
                var relative = partner.Key == MusicKey.CentreOf(MusicKey.Of(source).Relative);

                Assert.True(
                    relative ? partner.Shift is 0 : landed is 0 or 5 or 7,
                    $"{partner.Key.Name} at {partner.Shift} for {source.Name}"
                );
            }
        }
    }

    [Fact]
    public void The_widest_shift_is_the_least_that_reaches_every_key_from_every_key()
    {
        foreach (var source in MusicKey.AllKeys.Select(MusicKey.CentreOf))
        {
            Assert.Equal(
                24,
                Partners.For(source, -Partners.WidestShift, Partners.WidestShift).Count
            );
            Assert.NotEqual(
                24,
                Partners.For(source, 1 - Partners.WidestShift, Partners.WidestShift - 1).Count
            );
        }
    }

    [Fact]
    public void A_span_away_from_0_lists_keys_at_their_alt_shifts()
    {
        var rows = Partners.For(Centre("A", DiatonicMode.Dorian), 3, 5);

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.InRange(row.Shift, 3, 5));
        Assert.All(rows, row => Assert.Null(row.Relationship));
        Assert.Contains(rows, row => row.Key.Name == "G major" && row.Shift is 5);
    }

    [Fact]
    public void A_key_is_listed_at_its_in_range_shift_closest_to_0()
    {
        foreach (var row in Partners.For(Centre("C", DiatonicMode.Ionian), -1, 3))
        {
            Assert.InRange(row.Shift, -1, 3);
            Assert.All(
                row.AltShifts.Where(alt => alt is >= -1 and <= 3),
                alt => Assert.True(Math.Abs(alt) >= row.AbsoluteShift)
            );
        }
    }

    [Fact]
    public void Alt_shifts_are_a_fifth_either_way_and_exclude_the_listed_one()
    {
        foreach (var partner in Partners.For(Centre("D#", DiatonicMode.Ionian), -6, 6))
        {
            Assert.DoesNotContain(partner.Shift, partner.AltShifts);
            Assert.All(partner.AltShifts, alt => Assert.InRange(alt, -6, 6));
        }
    }

    [Fact]
    public void A_mixed_verdict_pairs_as_its_collapse()
    {
        var basis = Partners.Basis(Centre("A", DiatonicMode.Dorian), mixed: true);

        Assert.Equal(Centre("A", DiatonicMode.Aeolian), basis);
        Assert.Equal(
            ["A minor", "C major"],
            Partners.For(basis).Where(row => row.NotesShared is 7).Select(row => row.Key.Name)
        );
    }

    [Theory]
    [InlineData(-7, 0)]
    [InlineData(0, 7)]
    [InlineData(2, 1)]
    public void A_range_outside_6_or_upside_down_is_rejected(int lowest, int highest) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Partners.For(Centre("C", DiatonicMode.Ionian), lowest, highest)
        );

    [Fact]
    public void A_picked_modal_track_pairs_from_its_centre()
    {
        Assert.SkipUnless(TestLedger.HasReal, "No ledger.db on this machine.");

        var modal = TestLedger
            .FromReal()
            .Runs.Where(run => run is { IsCollapsed: true, Mixed: false })
            .ToList();

        Assert.SkipWhen(modal.Count is 0, "The ledger holds no modal verdict.");

        foreach (var run in modal)
        {
            var song = new LibrarySong
            {
                Song = run.Song,
                Name = run.DisplayName,
                Key = MusicKey.Of(run.Centre!),
                Run = run,
            };

            Assert.Equal(run.Centre, Partners.For(song.Centre!)[0].Key);
        }
    }

    private static TonalCentre Centre(string tonic, DiatonicMode mode) => new(tonic, mode);
}

public sealed class MusicKeyTests
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
    public void A_key_round_trips_through_a_centre()
    {
        foreach (var key in MusicKey.AllKeys)
            Assert.Equal(key, MusicKey.Of(MusicKey.CentreOf(key)));
    }

    [Fact]
    public void A_modal_centre_is_tagged_by_its_collapse() =>
        Assert.Equal(
            MusicKey.FromString("A minor"),
            MusicKey.Of(new TonalCentre("A", DiatonicMode.Dorian))
        );

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
