using Crateside.Core.Marks;

namespace Crateside.Tests.Marks;

public sealed class TrackMarkStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string StorePath => Path.Combine(_folder, "track-marks.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void A_mark_survives_a_restart()
    {
        new TrackMarkStore(StorePath).Set("artist - title", TrackMark.ChangesKey, true);

        Assert.Equal([TrackMark.ChangesKey], new TrackMarkStore(StorePath).Of("artist - title"));
    }

    [Fact]
    public void Songs_are_found_whatever_the_case_they_were_marked_in()
    {
        new TrackMarkStore(StorePath).Set("Artist - Title", TrackMark.ChangesKey, true);

        Assert.Equal([TrackMark.ChangesKey], new TrackMarkStore(StorePath).Of("artist - title"));
    }

    [Fact]
    public void Clearing_the_last_mark_forgets_the_song()
    {
        var store = new TrackMarkStore(StorePath);
        store.Set("song", TrackMark.ChangesKey, true);

        store.Set("song", TrackMark.ChangesKey, false);

        Assert.Empty(new TrackMarkStore(StorePath).Read());
        Assert.Empty(new TrackMarkStore(StorePath).Of("song"));
    }

    [Fact]
    public void Marking_twice_records_it_once()
    {
        var store = new TrackMarkStore(StorePath);

        store.Set("song", TrackMark.ChangesKey, true);
        store.Set("song", TrackMark.ChangesKey, true);

        Assert.Equal([TrackMark.ChangesKey], new TrackMarkStore(StorePath).Of("song"));
    }

    [Fact]
    public void An_unreadable_file_reads_as_no_marks()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StorePath, "{ not json");

        Assert.Empty(new TrackMarkStore(StorePath).Read());
    }
}
