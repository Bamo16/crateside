using Crateside.Core.Ledger;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// Finding one track in a list that has grown past reading. The list is client-side and the
/// pills count the whole set, so the only thing search has to get right is what counts as a
/// match and where to point when there is none here.
/// </summary>
public class TrackSearchTests
{
    private static readonly TrackRow Staged = new()
    {
        VideoId = "aaaaaaaaaaa",
        Title = "Ludacris, Pharrell - Southern Hospitality",
        Samples = [new TrackSample { Label = "Vocal - Full", AbletonKey = "B major" }],
    };

    private static readonly TrackRow Finished = new()
    {
        VideoId = "bbbbbbbbbbb",
        Title = "TLC - No Scrubs",
        Verdict = new LedgerRun
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "TLC - No Scrubs",
            Key = "F# minor",
        },
        Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
    };

    private static readonly TrackRow Renamed = new()
    {
        VideoId = "ccccccccccc",
        Title = "topic - Provided to YouTube",
        DerivedTitle = "Diana Ross - Upside Down",
        Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
    };

    private static readonly TrackRow[] All = [Staged, Finished, Renamed];

    [Theory]
    [InlineData("southern")]
    [InlineData("SOUTHERN")]
    [InlineData("  southern  ")]
    [InlineData("Ludacris, Pharrell")]
    public void A_track_is_found_by_part_of_its_name(string search) =>
        Assert.True(Staged.Matches(search));

    [Fact]
    public void An_empty_search_matches_everything()
    {
        Assert.All(All, row => Assert.True(row.Matches(string.Empty)));
        Assert.All(All, row => Assert.True(row.Matches("   ")));
        Assert.All(All, row => Assert.True(row.Matches(null)));
    }

    /// <summary>
    /// A renamed track is findable by either name, because the user knows one of them and not
    /// necessarily the one the list is showing.
    /// </summary>
    [Fact]
    public void A_renamed_track_answers_to_both_its_names()
    {
        Assert.True(Renamed.Matches("Upside Down"));
        Assert.True(Renamed.Matches("Provided to YouTube"));
    }

    [Fact]
    public void A_track_is_found_by_its_key()
    {
        Assert.True(Finished.Matches("F# minor"));
        Assert.False(Staged.Matches("F# minor"));
    }

    [Fact]
    public void A_track_is_found_by_its_video_id() => Assert.True(Finished.Matches("bbbbbbbbbbb"));

    [Fact]
    public void Nothing_matches_a_term_no_row_carries() =>
        Assert.All(All, row => Assert.False(row.Matches("nonesuch")));

    [Fact]
    public void The_filters_holding_a_match_are_named_so_a_fruitless_search_can_point_somewhere()
    {
        Assert.Equal([TrackFilter.InStaging], All.Holding("southern"));
    }

    /// <summary>
    /// A finished track is in no named view, so All is the only honest place to send anyone —
    /// and without this the search would say nothing matches a track that plainly does.
    /// </summary>
    [Fact]
    public void All_is_offered_only_when_nothing_more_specific_holds_the_match()
    {
        Assert.Equal([TrackFilter.All], All.Holding("scrubs"));
        Assert.DoesNotContain(TrackFilter.All, All.Holding("southern"));
    }

    /// <summary>
    /// Unfinished holds the staged track, but every row in it is already in a named view, so
    /// offering it would be a second answer to a question that has a better first one.
    /// </summary>
    [Fact]
    public void Unfinished_is_never_offered_as_somewhere_to_look() =>
        Assert.DoesNotContain(TrackFilter.Unfinished, All.Holding("southern"));

    [Fact]
    public void A_search_nothing_answers_holds_no_filters() =>
        Assert.Empty(All.Holding("nonesuch"));

    /// <summary>
    /// The counts on the pills are taken over every row, so narrowing the list must not narrow
    /// them — searching tells you what is here, not what exists.
    /// </summary>
    [Fact]
    public void Searching_does_not_change_what_the_pills_count()
    {
        var before = All.Count(TrackFilter.All);

        var shown = All.Where(row => row.Matches("southern")).ToArray();

        Assert.Single(shown);
        Assert.Equal(before, All.Count(TrackFilter.All));
    }
}
