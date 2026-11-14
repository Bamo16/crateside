using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Ableton;

public sealed class BrowserCollectionsTests
{
    [Fact]
    public void A_new_warp_state_replaces_the_old_and_the_users_own_Collections_stay()
    {
        var merged = CollectionSettings.Maintainers.Merged(
            [1, 2],
            [BrowserCollection.Warped],
            BrowserCollections.Cratesides
        );

        Assert.Equal([1, 4], merged);
    }

    [Fact]
    public void Replacing_only_the_track_marks_leaves_the_warp_state_where_it_was()
    {
        var merged = CollectionSettings.Maintainers.Merged(
            [4, 6],
            [],
            BrowserCollections.TrackMarks
        );

        Assert.Equal([4], merged);
    }

    [Fact]
    public void Every_meaning_crateside_sets_is_one_it_takes_away()
    {
        Assert.Equal(
            Enum.GetValues<BrowserCollection>().Order(),
            BrowserCollections.Cratesides.Order()
        );
    }

    [Fact]
    public void With_every_meaning_off_a_file_s_Collections_are_returned_untouched()
    {
        var existing = new[] { 5, 2, 7 };

        var merged = new CollectionSettings().Merged(
            existing,
            [BrowserCollection.Warped, BrowserCollection.OddBars],
            BrowserCollections.Cratesides
        );

        Assert.Same(existing, merged);
    }

    [Fact]
    public void Only_the_numbers_a_meaning_is_mapped_to_are_added_or_removed()
    {
        var onlyOddBars = new CollectionSettings { OddBars = 3 };

        var added = onlyOddBars.Merged(
            [1, 2, 7],
            [BrowserCollection.Warped, BrowserCollection.OddBars],
            BrowserCollections.Cratesides
        );
        var removed = onlyOddBars.Merged([1, 2, 3, 7], [], BrowserCollections.Cratesides);

        Assert.Equal([1, 2, 3, 7], added);
        Assert.Equal([1, 2, 7], removed);
    }

    [Fact]
    public void Two_meanings_on_one_number_are_refused()
    {
        var settings = new CollectionSettings { CheckBeat = 4, Warped = 4 };

        Assert.Contains("Collection 4", settings.Problem());
        Assert.Throws<ArgumentException>(() =>
            new CollectionSettingsStore(
                Path.Combine(Path.GetTempPath(), "crateside-tests-absent", "x.json"),
                TestLedger.Store()
            ).Save(settings)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void A_number_outside_Lives_seven_is_refused(int number) =>
        Assert.NotNull(new CollectionSettings { OddBars = number }.Problem());

    [Fact]
    public void Distinct_numbers_and_none_stand() =>
        Assert.Null(
            new CollectionSettings
            {
                CheckBeat = 1,
                Warped = 7,
                KeyCaveat = null,
            }.Problem()
        );

    [Fact]
    public void A_track_marked_odd_bars_is_put_in_that_Collection_and_a_hand_set_one_goes_when_it_is_not()
    {
        Assert.Equal(
            [BrowserCollection.OddBars],
            BrowserCollections.TrackMarksFor(null, [TrackMark.OddBars])
        );
        Assert.Equal(
            [BrowserCollection.KeyCaveat, BrowserCollection.OddBars],
            BrowserCollections.TrackMarksFor(null, [TrackMark.OddBars, TrackMark.ChangesKey])
        );
        Assert.Equal(
            [1],
            CollectionSettings.Maintainers.Merged(
                [1, 7],
                BrowserCollections.TrackMarksFor(null),
                BrowserCollections.Cratesides
            )
        );
    }

    [Theory]
    [InlineData(KeyConfidence.Low, DiatonicMode.Aeolian, true)]
    [InlineData(KeyConfidence.Withheld, DiatonicMode.Aeolian, true)]
    [InlineData(KeyConfidence.High, DiatonicMode.Dorian, true)]
    [InlineData(KeyConfidence.Medium, DiatonicMode.Ionian, false)]
    [InlineData(KeyConfidence.High, DiatonicMode.Aeolian, false)]
    public void A_key_caveat_marks_a_verdict_whose_key_tag_may_mislead(
        KeyConfidence confidence,
        DiatonicMode mode,
        bool caveat
    )
    {
        var verdict = new LedgerRun
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            Center = new TonalCenter("A", mode),
            Confidence = confidence,
        };

        Assert.Equal(
            caveat,
            BrowserCollections.TrackMarksFor(verdict).Contains(BrowserCollection.KeyCaveat)
        );
    }

    [Fact]
    public void A_track_marked_as_changing_key_carries_the_caveat_whatever_the_verdict()
    {
        var verdict = new LedgerRun
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            Center = new TonalCenter("A", DiatonicMode.Aeolian),
            Confidence = KeyConfidence.High,
        };

        Assert.Empty(BrowserCollections.TrackMarksFor(verdict, []));
        Assert.Equal(
            [BrowserCollection.KeyCaveat],
            BrowserCollections.TrackMarksFor(verdict, [TrackMark.ChangesKey])
        );
        Assert.Equal(
            [BrowserCollection.KeyCaveat],
            BrowserCollections.TrackMarksFor(null, [TrackMark.ChangesKey])
        );
    }

    [Fact]
    public void A_verdict_carries_the_caveat_for_a_section_in_another_center_only_once_something_places_it()
    {
        static LedgerRun Verdict(params Section[] sections) =>
            new()
            {
                Timestamp = DateTimeOffset.UnixEpoch,
                Song = "a - b",
                Center = new TonalCenter("E", DiatonicMode.Ionian),
                Confidence = KeyConfidence.High,
                Sections = sections,
            };
        var home = new TonalCenter("E", DiatonicMode.Ionian);

        Assert.Empty(
            BrowserCollections.TrackMarksFor(
                Verdict(new Section("Verse", home), new Section("Chorus", home))
            )
        );
        Assert.Empty(BrowserCollections.TrackMarksFor(Verdict(new Section("All", home))));
        Assert.Equal(
            [BrowserCollection.KeyCaveat],
            BrowserCollections.TrackMarksFor(
                Verdict(
                    new Section("Verse", home),
                    new Section("Outro", new TonalCenter("F#", DiatonicMode.Ionian))
                    {
                        Support = SectionSupport.HumanSource,
                    }
                )
            )
        );
        Assert.Empty(
            BrowserCollections.TrackMarksFor(
                Verdict(
                    new Section("Verse", home),
                    new Section("Outro", new TonalCenter("F#", DiatonicMode.Ionian))
                    {
                        Support = SectionSupport.Hypothesis,
                    }
                )
            )
        );
    }

    [Fact]
    public void A_song_the_ledger_has_not_seen_carries_no_caveat()
    {
        Assert.Empty(BrowserCollections.TrackMarksFor(null));
    }
}
