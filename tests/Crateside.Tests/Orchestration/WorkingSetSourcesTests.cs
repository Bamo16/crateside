using Crateside.Core.Orchestration;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration;

public sealed class WorkingSetSourcesTests : IDisposable
{
    private static readonly DateTime Long = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _base = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)
    );

    private string Root => Path.Combine(_base, "working");

    private string Imports => Path.Combine(_base, "imports");

    private WorkingSet Set => field ??= new WorkingSet(Root, _time);

    public void Dispose()
    {
        if (Directory.Exists(_base))
            Directory.Delete(_base, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Sources_names_the_same_file_TryGetSource_does_for_every_track()
    {
        var lone = Put(Root, "lone", "Song.flac");
        Put(Root, "lone", "evidence.json");
        Put(Root, "lone", "partial.tmp");
        Put(Root, "pair", "A.flac");
        Put(Root, "pair", "B.flac");
        Put(Root, "none", "evidence.json");
        var imported = Put(Imports, "imported", "Original.wav");
        Put(Root, "both", "Download.flac");
        Put(Imports, "both", "Original.wav");
        Directory.CreateDirectory(Path.Combine(Root, "empty"));

        var sources = Set.Sources();

        foreach (
            var trackKey in new[] { "lone", "pair", "none", "imported", "both", "empty", "absent" }
        )
            Assert.Equal(
                Set.TryGetSource(trackKey, out var expected) ? expected : null,
                sources.GetValueOrDefault(trackKey)
            );

        Assert.Equal(lone, sources["lone"]);
        Assert.Equal(imported, sources["imported"]);
        Assert.False(sources.ContainsKey("pair"));
    }

    [Fact]
    public void A_folder_whose_modified_time_moved_is_listed_again()
    {
        var first = Put(Root, "track", "First.flac");
        Settle("track");

        Assert.Equal(first, Set.Sources()["track"]);

        File.Delete(first);
        var second = Put(Root, "track", "Second.flac");
        Directory.SetLastWriteTimeUtc(Path.Combine(Root, "track"), Long.AddDays(1));

        Assert.Equal(second, Set.Sources()["track"]);
    }

    [Fact]
    public void A_folder_whose_modified_time_has_not_moved_is_not_listed_again()
    {
        var first = Put(Root, "track", "First.flac");
        Settle("track");

        Assert.Equal(first, Set.Sources()["track"]);

        File.Delete(first);
        Put(Root, "track", "Second.flac");
        Settle("track");

        Assert.Equal(first, Set.Sources()["track"]);
    }

    [Fact]
    public void A_folder_changed_moments_ago_is_listed_again_even_at_the_same_modified_time()
    {
        var first = Put(Root, "track", "First.flac");
        var written = Directory.GetLastWriteTimeUtc(Path.Combine(Root, "track"));
        _time.SetUtcNow(new DateTimeOffset(written, TimeSpan.Zero).AddMilliseconds(500));

        Assert.Equal(first, Set.Sources()["track"]);

        File.Delete(first);
        var second = Put(Root, "track", "Second.flac");
        Directory.SetLastWriteTimeUtc(Path.Combine(Root, "track"), written);

        Assert.Equal(second, Set.Sources()["track"]);
    }

    [Fact]
    public void A_removed_folder_drops_out()
    {
        Put(Root, "track", "Song.flac");
        Settle("track");

        Assert.True(Set.Sources().ContainsKey("track"));

        Directory.Delete(Path.Combine(Root, "track"), recursive: true);

        Assert.Empty(Set.Sources());
    }

    private static string Put(string root, string trackKey, string fileName)
    {
        var folder = Path.Combine(root, trackKey);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "x");

        return path;
    }

    private void Settle(string trackKey) =>
        Directory.SetLastWriteTimeUtc(Path.Combine(Root, trackKey), Long);
}
