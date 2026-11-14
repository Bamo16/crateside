using Crateside.Core.Genres;

namespace Crateside.Tests.Genres;

public sealed class GenreGapsTests
{
    [Fact]
    public void Labels_the_table_does_not_map_are_counted_by_the_songs_that_carry_them()
    {
        GenreRecord[] records =
        [
            new()
            {
                Song = "goreshit - toromi hearts 2",
                Heard = ["tinkle core", "Breakbeat", "soundtrack"],
            },
            new() { Song = "kubbi - pathfinder", Heard = ["ambient", "Tinkle Core", "Soundtrack"] },
            new() { Song = "cypress hill - hits from the bong", Heard = ["hip hop"] },
            new() { Song = "fox stevenson - glue gun", Heard = ["drum and bass", "dance"] },
        ];

        Assert.Equal(
            [new GenreGap("soundtrack", 2), new GenreGap("tinkle core", 2)],
            GenreGaps.From(records)
        );
    }

    [Fact]
    public void A_song_the_sources_were_never_asked_about_is_not_a_gap() =>
        Assert.Empty(GenreGaps.From([new GenreRecord { Song = "unasked" }]));
}
