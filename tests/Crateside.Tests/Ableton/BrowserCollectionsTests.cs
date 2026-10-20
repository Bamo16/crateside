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
            [1, 2, 7],
            [BrowserCollection.Warped],
            BrowserCollections.Cratesides
        );

        Assert.Equal([1, 4, 7], merged);
    }

    [Fact]
    public void Replacing_only_the_track_marks_leaves_the_warp_state_where_it_was()
    {
        var merged = BrowserCollections.Merged([3, 6], [], BrowserCollections.TrackMarks);

        Assert.Equal([3], merged);
    }

    [Fact]
    public void Odd_bars_stays_the_users_until_crateside_can_mark_a_track_with_it()
    {
        Assert.DoesNotContain(BrowserCollection.OddBars, BrowserCollections.Cratesides);
        Assert.DoesNotContain(BrowserCollection.Mine, BrowserCollections.Cratesides);
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
    public void A_song_the_ledger_has_not_seen_carries_no_caveat()
    {
        Assert.Empty(BrowserCollections.TrackMarksFor(null));
    }
}
