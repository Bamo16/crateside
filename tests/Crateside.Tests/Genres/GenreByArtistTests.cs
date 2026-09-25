using Crateside.Core.Genres;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Genres;

public class GenreByArtistTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string StorePath => Path.Combine(_directory, "genres.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Three_agreeing_answers_settle_the_artist_and_two_do_not()
    {
        var learned = GenreByArtist.Learn([
            ("Kanye West", "Hip Hop"),
            ("kanye west", "Hip Hop"),
            ("Kanye West", "Hip Hop"),
            ("Snail's House", "Future Bass"),
            ("Snail's House", "Future Bass"),
        ]);

        Assert.Equal("Hip Hop", learned["Kanye West"]);
        Assert.False(learned.ContainsKey("Snail's House"));
    }

    [Fact]
    public void Any_disagreement_including_none_of_these_settles_nothing()
    {
        var learned = GenreByArtist.Learn([
            ("Lil Dicky", "Hip Hop"),
            ("Lil Dicky", "Hip Hop"),
            ("Lil Dicky", "Hip Hop"),
            ("Lil Dicky", null),
            ("Moe Shop", "Future Bass"),
            ("Moe Shop", "Future Bass"),
            ("Moe Shop", "J-Pop"),
        ]);

        Assert.Empty(learned);
    }

    [Fact]
    public void A_learned_genre_never_overrides_the_users_answer_and_can_be_taken_back()
    {
        var store = new GenreStore(StorePath);

        store.Settle("answered", "Pop");
        store.SettleFromArtist("answered", "Hip Hop", "Kanye West");
        store.SettleFromArtist("learned", "Hip Hop", "Kanye West");

        var records = store.Read();
        Assert.Equal("Pop", records["answered"].Written);
        Assert.True(records["answered"].IsTheUsersAnswer);
        Assert.Equal("Hip Hop", records["learned"].Written);
        Assert.False(records["learned"].IsTheUsersAnswer);

        store.Unlearn("learned");
        store.Unlearn("answered");

        records = new GenreStore(StorePath).Read();
        Assert.Null(records["learned"].Written);
        Assert.Equal("Pop", records["answered"].Written);
    }

    [Fact]
    public void Answering_a_learned_song_makes_it_the_users_own()
    {
        var store = new GenreStore(StorePath);

        store.SettleFromArtist("song", "Hip Hop", "Kanye West");
        store.Settle("song", "Pop");

        Assert.True(store.Read()["song"].IsTheUsersAnswer);
    }

    [Theory]
    [InlineData("Jay-Z, Kanye West", "Jay-Z")]
    [InlineData("Kirara Magic & Yuuna Nini", "Kirara Magic")]
    [InlineData("Estelle feat. Kanye West", "Estelle")]
    [InlineData("Snail's House", "Snail's House")]
    public void The_primary_artist_is_the_first_credited(string credited, string primary) =>
        Assert.Equal(primary, ArtistCredit.Primary(credited));
}
