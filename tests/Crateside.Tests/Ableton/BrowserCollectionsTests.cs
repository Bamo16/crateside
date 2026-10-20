using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;

namespace Crateside.Tests.Ableton;

public sealed class BrowserCollectionsTests
{
    [Fact]
    public void A_new_warp_state_replaces_the_old_and_the_users_own_Collections_stay()
    {
        var merged = BrowserCollections.Merged(
            [1, 2],
            [BrowserCollection.Warped],
            BrowserCollections.Cratesides
        );

        Assert.Equal([1, 4], merged);
    }

    [Fact]
    public void Replacing_only_the_track_marks_leaves_the_warp_state_where_it_was()
    {
        var merged = BrowserCollections.Merged([3, 6], [], BrowserCollections.TrackMarks);

        Assert.Equal([3], merged);
    }

    [Fact]
    public void Odd_bars_is_crateside_s_to_set_and_take_away_and_Mine_stays_the_users()
    {
        Assert.Contains(BrowserCollection.OddBars, BrowserCollections.Cratesides);
        Assert.DoesNotContain(BrowserCollection.Mine, BrowserCollections.Cratesides);
    }

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
            BrowserCollections.Merged(
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
            Centre = new TonalCentre("A", mode),
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
            Centre = new TonalCentre("A", DiatonicMode.Aeolian),
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
    public void A_verdict_with_sections_in_different_centres_carries_the_caveat()
    {
        LedgerRun Verdict(params Section[] sections) =>
            new()
            {
                Timestamp = DateTimeOffset.UnixEpoch,
                Song = "a - b",
                Centre = new TonalCentre("E", DiatonicMode.Ionian),
                Confidence = KeyConfidence.High,
                Sections = sections,
            };
        var home = new TonalCentre("E", DiatonicMode.Ionian);

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
                    new Section("Outro", new TonalCentre("F#", DiatonicMode.Ionian))
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
