using Crateside.Core.Genres;
using Crateside.Core.Tracks;
using Crateside.Tests.Ledger;
using static Crateside.Tests.Tracks.SongKeyFixture;

namespace Crateside.Tests.Tracks;

public sealed class SongKeyMigrationTests : IDisposable
{
    private readonly SongKeyFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_record_under_a_judged_upload_moves_to_its_song()
    {
        new GenreStore(_fixture.Decisions).Suggest(Video, ["Pop"], At);

        var moved = Migration().Run();

        Assert.Equal(1, moved);
        Assert.Equal([Song], new GenreStore(_fixture.Decisions).Read().Keys);
    }

    [Fact]
    public void A_second_run_moves_nothing()
    {
        new GenreStore(_fixture.Decisions).Suggest(Video, ["Pop"], At);
        Migration().Run();

        var moved = Migration().Run();

        Assert.Equal(0, moved);
        Assert.Equal([Song], new GenreStore(_fixture.Decisions).Read().Keys);
    }

    [Fact]
    public void A_store_with_nothing_to_move_is_left_untouched()
    {
        new GenreStore(_fixture.Decisions).Suggest("someone - something", ["Pop"], At);

        Assert.Equal(0, Migration().Run());
        Assert.Equal(["someone - something"], new GenreStore(_fixture.Decisions).Read().Keys);
    }

    private SongKeyMigration Migration() =>
        new(_fixture.Reader(), TestLedger.Store(_fixture.JudgedLines()));
}
