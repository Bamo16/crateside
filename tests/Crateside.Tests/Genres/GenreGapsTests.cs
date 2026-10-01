using Crateside.Core.Genres;

namespace Crateside.Tests.Genres;

public sealed class GenreGapsTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Songs_the_list_does_not_cover_are_counted_by_what_the_sources_called_them()
    {
        GenreRecord[] records =
        [
            new()
            {
                Song = "goreshit - toromi hearts 2",
                Heard = ["breakcore", "Breakbeat", "electronic"],
                IsSettled = true,
            },
            new()
            {
                Song = "kubbi - pathfinder",
                Heard = ["ambient", "bitpop", "Electronic"],
                LookedUpAt = At,
            },
            new()
            {
                Song = "cypress hill - hits from the bong",
                Heard = ["hip hop"],
                Suggested = "Hip Hop",
                LookedUpAt = At,
            },
            new()
            {
                Song = "fox stevenson - glue gun",
                Heard = ["drum and bass", "dance"],
                Genre = "Drum & Bass",
                IsSettled = true,
            },
        ];

        var gaps = GenreGaps.From(records);

        Assert.Equal(new GenreGap("electronic", 2), gaps[0]);
        Assert.Contains(new GenreGap("breakcore", 1), gaps);
        Assert.Contains(new GenreGap("ambient", 1), gaps);
        Assert.DoesNotContain(gaps, gap => gap.Label is "dance" or "hip hop");
    }

    [Fact]
    public void A_song_never_looked_up_is_not_a_gap()
    {
        Assert.Empty(GenreGaps.From([new GenreRecord { Song = "unasked" }]));
    }
}
