using Crateside.Core.Genres;

namespace Crateside.Tests.Genres;

public class GenreStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private static readonly DateTimeOffset At = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private string StorePath => Path.Combine(_directory, "genres.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_suggestion_is_the_first_label_that_maps_onto_the_list()
    {
        var store = new GenreStore(StorePath);

        store.Suggest("fox stevenson - glue gun", ["Dance", "drum and bass", "electronic"], At);

        var record = new GenreStore(StorePath).Read()["fox stevenson - glue gun"];

        Assert.Equal("Drum & Bass", record.Suggested);
        Assert.Equal(["Dance", "drum and bass", "electronic"], record.Heard);
        Assert.False(record.IsSettled);
        Assert.Null(record.Written);
    }

    [Fact]
    public void Only_a_settled_genre_is_written()
    {
        var store = new GenreStore(StorePath);

        store.Suggest("xg - woke up", ["Hip-Hop/Rap"], At);
        store.Settle("xg - woke up", "K-Pop");

        var record = new GenreStore(StorePath).Read()["xg - woke up"];

        Assert.Equal("Hip Hop", record.Suggested);
        Assert.Equal("K-Pop", record.Written);
    }

    [Fact]
    public void Settling_on_none_of_the_list_is_an_answer_too()
    {
        var store = new GenreStore(StorePath);

        store.Suggest("goreshit - toromi hearts 2", ["Breakbeat", "breakcore"], At);
        store.Settle("goreshit - toromi hearts 2", null);

        var record = store.Read()["goreshit - toromi hearts 2"];

        Assert.True(record.IsSettled);
        Assert.Null(record.Written);
    }

    [Fact]
    public void A_later_lookup_leaves_the_users_answer_alone()
    {
        var store = new GenreStore(StorePath);

        store.Settle("song", "Rock");
        store.Suggest("song", ["Pop"], At);

        Assert.Equal("Rock", store.Read()["song"].Written);
    }

    [Fact]
    public void A_genre_off_the_list_is_refused()
    {
        var store = new GenreStore(StorePath);

        Assert.Throws<ArgumentException>(() => store.Settle("song", "Breakcore"));
        Assert.Empty(store.Read());
    }

    [Fact]
    public void An_unreadable_store_reads_as_empty()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "{ not json");

        Assert.Empty(new GenreStore(StorePath).Read());
    }
}
