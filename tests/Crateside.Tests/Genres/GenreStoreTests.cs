using Crateside.Core.Genres;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Genres;

public sealed class GenreStoreTests : IDisposable
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private static readonly DateTimeOffset At = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        _ledger.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void What_the_sources_said_is_kept_as_styles_and_maps_onto_the_list()
    {
        new GenreStore(_ledger).Hear(
            "fox stevenson - glue gun",
            new GenreHearing(["Dance", "drum and bass", "boom bap"], "5b3a4c5d"),
            At
        );

        var record = new GenreStore(_ledger).Read()["fox stevenson - glue gun"];

        Assert.Equal(["Dance", "drum and bass", "boom bap"], record.Styles);
        Assert.Equal(["EDM", "Drum & Bass"], record.Genres);
        Assert.Equal("5b3a4c5d", record.RecordingMbid);
        Assert.Equal(At, record.LookedUpAt);
        Assert.False(record.IsTheUsersAnswer);
    }

    [Fact]
    public void A_later_lookup_keeps_the_recording_when_it_matches_none()
    {
        var store = new GenreStore(_ledger);

        store.Hear("song", new GenreHearing(["pop"], "mbid"), At);
        store.Hear("song", new GenreHearing(["pop"], null), At.AddDays(1));

        Assert.Equal("mbid", store.Read()["song"].RecordingMbid);
    }

    [Fact]
    public void A_choice_among_the_29_replaces_crateside_and_the_one_before_it()
    {
        var store = new GenreStore(_ledger);

        store.Hear("xg - woke up", new GenreHearing(["Hip-Hop/Rap"], null), At);
        store.Choose("xg - woke up", "Pop");
        store.Choose("xg - woke up", "Funk");

        var record = new GenreStore(_ledger).Read()["xg - woke up"];

        Assert.Equal(["Funk"], record.Genres);
        Assert.Equal(["Hip Hop"], record.FromSources);
        Assert.True(record.IsTheUsersAnswer);
    }

    [Fact]
    public void A_genre_of_the_persons_own_is_added_beside_crateside_and_never_replaces_it()
    {
        var store = new GenreStore(_ledger);

        store.Hear("song", new GenreHearing(["pop"], null), At);
        store.Choose("song", "Meme");

        var record = store.Read()["song"];

        Assert.Equal(["Pop", "Meme"], record.Genres);
        Assert.False(record.IsTheUsersAnswer);
    }

    [Fact]
    public void A_lookup_leaves_the_persons_genres_alone()
    {
        var store = new GenreStore(_ledger);

        store.Choose("song", "Rock");
        store.Choose("song", "Meme");
        store.Hear("song", new GenreHearing(["pop"], null), At);

        Assert.Equal(["Rock", "Meme"], store.Read()["song"].Genres);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Pop")]
    [InlineData("Not|This")]
    public void A_name_that_cannot_be_a_keyword_value_is_refused(string genre)
    {
        var store = new GenreStore(_ledger);

        Assert.Throws<ArgumentException>(() => store.Choose("song", genre));
        Assert.Empty(store.Read());
    }

    [Fact]
    public void A_song_whose_labels_name_three_genres_gets_the_two_most_of_them_name()
    {
        new GenreStore(_ledger).Hear(
            "song",
            new GenreHearing(["pop rap", "hip hop", "rap", "pop", "electronic"], null),
            At
        );

        Assert.Equal(["Hip Hop", "Pop"], new GenreStore(_ledger).Read()["song"].Genres);
    }

    [Fact]
    public void A_genre_asked_of_the_files_is_remembered_so_it_can_be_retired()
    {
        var store = new GenreStore(_ledger);

        store.Hear("song", new GenreHearing(["pop"], null), At);
        store.MarkTagged([("song", ["Pop"])]);
        store.Hear("song", new GenreHearing(["rock", "hard rock"], null), At.AddDays(1));

        var record = new GenreStore(_ledger).Read()["song"];

        Assert.Equal(["Rock"], record.Genres);
        Assert.Equal(["Pop"], record.Retired);
    }
}
