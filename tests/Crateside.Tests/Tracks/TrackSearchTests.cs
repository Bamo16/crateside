using Crateside.Core.Familiarity;
using Crateside.Core.Ledger;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>What counts as a match, and what the familiarity filter keeps.</summary>
public sealed class TrackSearchTests
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
            Center = new TonalCenter("F#", DiatonicMode.Aeolian),
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
    public void Searching_does_not_change_what_the_pills_count()
    {
        var before = All.Count(TrackFilter.All);

        var shown = All.Where(row => row.Matches("southern")).ToArray();

        Assert.Single(shown);
        Assert.Equal(before, All.Count(TrackFilter.All));
    }

    [Fact]
    public void The_familiarity_filter_keeps_only_the_rows_settled_that_way()
    {
        var mainstream = Finished with
        {
            Familiarity = new FamiliarityRecord
            {
                Song = "TLC - No Scrubs",
                Settled = "Mainstream",
            },
        };

        Assert.Equal(
            [mainstream],
            new[] { mainstream, Staged }.Where(row => row.HasFamiliarity("Mainstream"))
        );
        Assert.DoesNotContain(
            mainstream,
            new[] { mainstream, Staged }.Where(row => row.HasFamiliarity("Obscure"))
        );
        Assert.Equal(2, new[] { mainstream, Staged }.Count(row => row.HasFamiliarity(null)));
    }
}
