using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Genres;

public sealed class GenreByArtistTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    public void Dispose()
    {
        _ledger.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Three_agreeing_answers_settle_the_artist_and_two_do_not()
    {
        var learned = GenreByArtist.Learn([
            ("Kanye West", "Hip Hop"),
            ("kanye west", "Hip Hop"),
            ("Kanye West", "Hip Hop"),
            ("Snail's House", "EDM"),
            ("Snail's House", "EDM"),
        ]);

        Assert.Equal("Hip Hop", learned["Kanye West"]);
        Assert.False(learned.ContainsKey("Snail's House"));
    }

    [Fact]
    public void Any_disagreement_settles_nothing()
    {
        var learned = GenreByArtist.Learn([
            ("Lil Dicky", "Hip Hop"),
            ("Lil Dicky", "Hip Hop"),
            ("Lil Dicky", "Hip Hop"),
            ("Lil Dicky", "Pop"),
            ("Moe Shop", "EDM"),
            ("Moe Shop", "EDM"),
            ("Moe Shop", "Pop"),
        ]);

        Assert.Empty(learned);
    }

    [Fact]
    public void A_learned_genre_never_overrides_the_users_answer_and_can_be_taken_back()
    {
        var store = new GenreStore(_ledger);

        store.Choose("answered", "Pop");
        store.Learn("answered", "Hip Hop", "Kanye West");
        store.Learn("learned", "Hip Hop", "Kanye West");

        var records = store.Read();
        Assert.Equal(["Pop"], records["answered"].Genres);
        Assert.True(records["answered"].IsTheUsersAnswer);
        Assert.Equal(["Hip Hop"], records["learned"].Genres);
        Assert.False(records["learned"].IsTheUsersAnswer);

        store.Unlearn("learned");
        store.Unlearn("answered");

        records = new GenreStore(_ledger).Read();
        Assert.Empty(records["learned"].Genres);
        Assert.Equal(["Pop"], records["answered"].Genres);
    }

    [Fact]
    public void Answering_a_learned_song_makes_it_the_users_own()
    {
        var store = new GenreStore(_ledger);

        store.Learn("song", "Hip Hop", "Kanye West");
        store.Choose("song", "Pop");

        var record = store.Read()["song"];

        Assert.True(record.IsTheUsersAnswer);
        Assert.Null(record.Learned);
    }

    [Fact]
    public void A_learned_genre_gives_way_to_what_the_songs_own_sources_map_onto()
    {
        var store = new GenreStore(_ledger);

        store.Learn("song", "Hip Hop", "Kanye West");
        store.Hear("song", new GenreHearing(["drum and bass"], null), At);

        Assert.Equal(["Drum & Bass"], store.Read()["song"].Genres);
    }

    [Fact]
    public void A_learned_genre_stands_where_the_sources_map_onto_nothing()
    {
        var store = new GenreStore(_ledger);

        store.Learn("song", "Hip Hop", "Kanye West");
        store.Hear("song", new GenreHearing(["soundtrack"], null), At);

        Assert.Equal(["Hip Hop"], store.Read()["song"].Genres);
    }

    [Theory]
    [InlineData("Jay-Z, Kanye West", "Jay-Z")]
    [InlineData("Kirara Magic & Yuuna Nini", "Kirara Magic")]
    [InlineData("Estelle feat. Kanye West", "Estelle")]
    [InlineData("Snail's House", "Snail's House")]
    public void The_primary_artist_is_the_first_credited(string credited, string primary) =>
        Assert.Equal(primary, ArtistCredit.Primary(credited));
}
