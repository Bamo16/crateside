using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using static Crateside.Tests.Tracks.SongKeyFixture;

namespace Crateside.Tests.Tracks;

public sealed class TrackAnnotationReaderTests : IDisposable
{
    private readonly SongKeyFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_genre_looked_up_before_the_verdict_is_suggested_on_the_judged_row()
    {
        new GenreStore(_fixture.Decisions).Suggest(Video, ["Pop", "country pop"], At);

        var row = Row(_fixture.Judged(), _fixture.Reader());

        Assert.Equal(Song, row.Song);
        Assert.Equal("Pop", row.Genre?.Suggested);
        Assert.Equal(At, row.Genre?.LookedUpAt);
    }

    [Fact]
    public void A_genre_settled_on_the_judged_row_reads_back_after_a_restart()
    {
        GenreStore genres = new(_fixture.Decisions);
        genres.Suggest(Video, ["Pop", "country pop"], At);
        var ledger = _fixture.Judged();

        genres.Settle(Row(ledger, _fixture.Reader(genres)).Song, "Pop");

        Assert.Equal("Pop", Row(ledger, _fixture.Reader()).Genre?.Written);
        Assert.Equal([Song], new GenreStore(_fixture.Decisions).Read().Keys);
    }

    [Fact]
    public void An_answer_given_before_the_verdict_survives_it()
    {
        new GenreStore(_fixture.Decisions).Settle(Video, "Rock");
        new FamiliarityStore(_fixture.Decisions).Settle(Video, "Mainstream");
        new TrackMarkStore(_fixture.Decisions).Set(Video, TrackMark.ChangesKey, true);

        var row = Row(_fixture.Judged(), _fixture.Reader());

        Assert.Equal("Rock", row.Genre?.Written);
        Assert.Equal("Mainstream", row.Familiarity?.Settled);
        Assert.Equal([TrackMark.ChangesKey], row.Marks);
    }

    [Fact]
    public void The_users_answer_under_the_upload_outranks_a_suggestion_under_the_song()
    {
        GenreStore genres = new(_fixture.Decisions);
        genres.Settle(Video, "Rock");
        genres.Suggest(Song, ["Pop"], At);

        var record = Row(_fixture.Judged(), _fixture.Reader()).Genre;

        Assert.Equal("Rock", record?.Written);
        Assert.Equal("Pop", record?.Suggested);
    }

    [Fact]
    public void The_users_answer_under_the_song_outranks_one_under_the_upload()
    {
        GenreStore genres = new(_fixture.Decisions);
        genres.Settle(Video, "Rock");
        genres.Settle(Song, "Pop");

        Assert.Equal("Pop", Row(_fixture.Judged(), _fixture.Reader()).Genre?.Written);
    }

    [Fact]
    public void An_unjudged_uploads_records_stay_under_its_video_id()
    {
        new GenreStore(_fixture.Decisions).Suggest(Video, ["Pop"], At);

        var row = Row(_fixture.Unjudged(), _fixture.Reader());

        Assert.Equal(Video, row.Song);
        Assert.Equal("Pop", row.Genre?.Suggested);
        Assert.Equal([Video], new GenreStore(_fixture.Decisions).Read().Keys);
    }

    private TrackRow Row(KeyLedger ledger, TrackAnnotationReader reader) =>
        Assert.Single(
            TracksView
                .Compose(
                    WorkListsView.Compose(
                        [
                            new WorkItem
                            {
                                PlaylistItemId = $"PLI-{Video}",
                                TrackKey = Video,
                                Title = "Style",
                            },
                        ],
                        new HashSet<string>(),
                        new HashSet<string>(),
                        new HashSet<string>(),
                        new Dictionary<string, TrackInstruction>()
                    ),
                    ledger,
                    [],
                    new Dictionary<string, IReadOnlyList<StemOnDisk>>(),
                    @"D:\Music\Library",
                    new JobStore(Path.Combine(_fixture.Folder, "jobs.jsonl")),
                    new Dictionary<string, TrackInstruction>(),
                    reader.Read(ledger)
                )
                .Rows
        );
}
