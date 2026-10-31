using Crateside.Core.Genres;
using Crateside.Core.Tracks;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Time.Testing;
using static Crateside.Tests.Tracks.SongKeyFixture;

namespace Crateside.Tests.Tracks;

public sealed class SongKeyMigrationTests : IDisposable
{
    private readonly SongKeyFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_record_under_a_judged_upload_moves_to_its_song_with_the_old_file_kept_aside()
    {
        new GenreStore(_fixture.GenresPath).Suggest(Video, ["Pop"], At);
        var before = File.ReadAllText(_fixture.GenresPath);

        var moved = Migration(new FakeTimeProvider(At)).Run();

        Assert.Equal(1, moved);
        Assert.Equal([Song], new GenreStore(_fixture.GenresPath).Read().Keys);
        Assert.Equal(before, File.ReadAllText(Assert.Single(Backups())));
    }

    [Fact]
    public void A_second_run_moves_nothing_and_keeps_no_second_backup()
    {
        new GenreStore(_fixture.GenresPath).Suggest(Video, ["Pop"], At);
        FakeTimeProvider time = new(At);
        Migration(time).Run();
        var after = File.ReadAllText(_fixture.GenresPath);
        time.Advance(TimeSpan.FromMinutes(1));

        var moved = Migration(time).Run();

        Assert.Equal(0, moved);
        Assert.Single(Backups());
        Assert.Equal(after, File.ReadAllText(_fixture.GenresPath));
    }

    [Fact]
    public void A_store_with_nothing_to_move_is_left_untouched()
    {
        new GenreStore(_fixture.GenresPath).Suggest("someone - something", ["Pop"], At);

        Assert.Equal(0, Migration(new FakeTimeProvider(At)).Run());
        Assert.Empty(Backups());
    }

    private SongKeyMigration Migration(FakeTimeProvider time) =>
        new(_fixture.Reader(), TestLedger.Store(_fixture.JudgedLines()), time);

    private string[] Backups() => Directory.GetFiles(_fixture.Folder, "genres.json.bak-*");
}
