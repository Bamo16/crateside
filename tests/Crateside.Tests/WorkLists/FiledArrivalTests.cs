using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.WorkLists;

/// <summary>A track added to the playlist whose song is already filed is said to be (#84).</summary>
public sealed class FiledArrivalTests : IDisposable
{
    private const string Filed = "TaQwW5eQZeY";
    private const string OtherUpload = "Zb2mXyVq1Rc";
    private const string NewSong = "p3NZn0mA_XI";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public FiledArrivalTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "Mashup Samples"));
        Directory.CreateDirectory(Path.Combine(_directory, "Sample Staging"));
    }

    private string Mashup => Path.Combine(_directory, "Mashup Samples");

    private string Staging => Path.Combine(_directory, "Sample Staging");

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_new_entry_arrives_and_one_already_waiting_does_not()
    {
        var store = Store();
        store.Record([Item(Filed, "entry-1")]);

        var arrivals = store.Arrive([Item(Filed, "entry-1"), Item(NewSong, "entry-2")]);

        Assert.Equal([NewSong], arrivals.Select(item => item.VideoId));
    }

    [Fact]
    public void An_entry_crateside_removed_and_the_user_added_again_arrives_and_is_put_back_in_line()
    {
        var store = Store();
        store.Record([Item(Filed, "entry-1")]);
        store.MarkRemovalAttempted(
            [Filed],
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { [Filed] = "entry-1" }
        );

        var arrivals = store.Arrive([Item(Filed, "entry-9")]);

        Assert.Equal([Filed], arrivals.Select(item => item.VideoId));
        Assert.Null(Assert.Single(Store().Read()).RemovalAttemptedAt);
    }

    [Fact]
    public void The_removed_entry_read_again_from_a_stale_playlist_is_not_an_arrival()
    {
        var store = Store();
        store.Record([Item(Filed, "entry-1")]);
        store.MarkRemovalAttempted(
            [Filed],
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { [Filed] = "entry-1" }
        );

        Assert.Empty(store.Arrive([Item(Filed, "entry-1")]));
        Assert.NotNull(Assert.Single(Store().Read()).RemovalAttemptedAt);
    }

    [Fact]
    public void An_entry_whose_removal_failed_stays_left_rather_than_arriving_again()
    {
        var store = Store();
        store.Record([Item(Filed, "entry-1")]);
        store.MarkRemovalAttempted(
            [Filed],
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { [Filed] = "entry-1" }
        );
        store.RecordRemovalFailures(
            new Dictionary<string, RemovalFailure> { [Filed] = new() { Reason = "forbidden" } }
        );

        Assert.Empty(store.Arrive([Item(Filed, "entry-1")]));
    }

    [Fact]
    public void An_upload_with_stems_in_mashup_samples_is_already_filed_there()
    {
        Stem(Mashup, "How We Do (Vocal - Full).flac", Filed);

        Assert.Equal(
            "in Mashup Samples",
            Where(Item(Filed, "entry-1", "The Game, 50 Cent - How We Do"))
        );
    }

    [Fact]
    public void A_different_upload_of_a_filed_song_looks_like_one_in_its_folder()
    {
        Stem(Staging, "How We Do (Vocal - Full).flac", Filed);

        Assert.Equal(
            "looks like one in Sample Staging",
            Where(Item(OtherUpload, "entry-2", "The Game, 50 Cent - How We Do (Official Video)"))
        );
    }

    [Fact]
    public void A_song_with_no_files_is_not_filed()
    {
        Stem(Mashup, "How We Do (Vocal - Full).flac", Filed);

        Assert.Null(Where(Item(NewSong, "entry-3", "Fox Stevenson - Glue Gun")));
    }

    private string? Where(QueueItem arrival)
    {
        var ledger = Ledger();

        return QueueService.FiledWhere(
            arrival,
            ProcessedTracks.Scan(ledger, [Staging, Mashup]),
            ledger,
            Mashup
        );
    }

    private KeyLedger Ledger()
    {
        var path = Path.Combine(_directory, "runs.jsonl");

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-08T00:00:00+00:00",
                    song = "the game, 50 cent - how we do",
                    artist = "The Game, 50 Cent",
                    title = "How We Do",
                    source_url = $"https://music.youtube.com/watch?v={Filed}",
                    verdict = new { key = "A minor", confidence = "HIGH" },
                }
            )
        );

        return KeyLedger.Read(path);
    }

    private static void Stem(string root, string name, string videoId)
    {
        var path = Path.Combine(root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.4.0.0 | preset: Vocal - Full | source: https://www.youtube.com/watch?v={videoId}";
        file.Save();
    }

    private IntakeStore Store() => new(Path.Combine(_directory, "intake.json"));

    private static QueueItem Item(string videoId, string entry, string title = "Some Song") =>
        new()
        {
            VideoId = videoId,
            Title = title,
            PlaylistItemId = entry,
        };
}
