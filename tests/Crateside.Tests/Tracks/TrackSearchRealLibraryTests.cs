using Crateside.Core.Tracks;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Tracks;

/// <summary>The near-match cutoff, measured against the songs the real ledger knows (#230).</summary>
public sealed class TrackSearchRealLibraryTests
{
    [Theory]
    [InlineData("pop lokc", "Pop, Lock")]
    [InlineData("beyonse", "Beyonc")]
    [InlineData("royksop", "yksopp")]
    [InlineData("shimomora", "Shimomura")]
    public void A_misspelling_finds_its_song_among_the_near_matches(string typed, string expected)
    {
        var found = NearMatches(RealRows(), typed);

        Assert.Contains(
            found,
            row => row.Title.Contains(expected, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Theory]
    [InlineData("xqzv")]
    [InlineData("asdf")]
    [InlineData("qwertyuiop")]
    public void Nonsense_scores_no_row_as_a_near_match(string typed) =>
        Assert.Empty(NearMatches(RealRows(), typed));

    [Fact]
    public void A_common_word_is_an_exact_search_so_the_near_tier_never_runs()
    {
        var result = RealRows().Search("the");

        Assert.False(result.NearMatches);
        Assert.NotEmpty(result.Rows);
    }

    private static TrackRow[] NearMatches(TrackRow[] rows, string typed) =>
        [
            .. rows.Where(row =>
                TrackSearch.Score(row, TrackSearch.Fold(typed)) >= TrackSearch.NearCutoff
            ),
        ];

    private static TrackRow[] RealRows()
    {
        Assert.SkipUnless(TestLedger.HasReal, "No ledger.db on this machine.");

        return
        [
            .. TestLedger
                .FromReal()
                .Runs.Where(run => run.Artist is { Length: > 0 })
                .GroupBy(run => run.Song)
                .Select(song => song.Last())
                .Select(run => new TrackRow
                {
                    TrackKey = run.SourceTrackKey ?? run.Song,
                    Title = run.DisplayName,
                    Verdict = run,
                }),
        ];
    }
}
