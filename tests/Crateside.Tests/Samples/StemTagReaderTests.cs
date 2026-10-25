using Crateside.Core.Samples;

namespace Crateside.Tests.Samples;

public sealed class StemTagReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n"),
        "stem.flac"
    );

    public StemTagReaderTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), _path);
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_file_read_once_is_read_again_after_it_is_rewritten()
    {
        Title("Before");
        Assert.Equal("Before", StemTagReader.Read(_path)?.Title);

        Title("Afters");
        Assert.Equal("Afters", StemTagReader.Read(_path)?.Title);
    }

    [Fact]
    public void A_deleted_file_reads_as_nothing()
    {
        Title("Gone");
        Assert.NotNull(StemTagReader.Read(_path));

        File.Delete(_path);

        Assert.Null(StemTagReader.Read(_path));
    }

    [Fact]
    public void Saved_tags_are_used_after_a_restart_while_the_file_is_unchanged()
    {
        var cache = Path.Combine(Path.GetDirectoryName(_path)!, "stem-tags.json");
        Title("Real");
        var info = new FileInfo(_path);
        File.WriteAllText(
            cache,
            $$"""
            {"entries":[{"path":{{System.Text.Json.JsonSerializer.Serialize(
                _path
            )}},"length":{{info.Length}},"written":"{{info.LastWriteTimeUtc:O}}","title":"Kept","comment":"stemforge/0.3.1.0 | preset: Drums | source: https://www.youtube.com/watch?v=dQw4w9WgXcQ"}]}
            """
        );

        StemTagReader.Load(cache);

        Assert.Equal("Kept", StemTagReader.Read(_path)?.Title);
        Assert.Equal("dQw4w9WgXcQ", StemTagReader.Read(_path)?.SourceVideoId);
    }

    [Fact]
    public void Saved_tags_of_a_file_that_changed_since_are_read_again()
    {
        var cache = Path.Combine(Path.GetDirectoryName(_path)!, "stem-tags.json");
        Title("Real");
        var info = new FileInfo(_path);
        File.WriteAllText(
            cache,
            $$"""
            {"entries":[{"path":{{System.Text.Json.JsonSerializer.Serialize(
                _path
            )}},"length":{{info.Length}},"written":"{{info.LastWriteTimeUtc:O}}","title":"Kept"}]}
            """
        );
        StemTagReader.Load(cache);
        Title("Newer");

        Assert.Equal("Newer", StemTagReader.Read(_path)?.Title);
    }

    [Fact]
    public void Tags_read_are_saved_for_the_next_start()
    {
        var cache = Path.Combine(Path.GetDirectoryName(_path)!, "stem-tags.json");
        Title("Saved");
        Assert.Equal("Saved", StemTagReader.Read(_path)?.Title);

        StemTagReader.Save(cache);

        Assert.Contains("Saved", File.ReadAllText(cache));
    }

    private void Title(string title)
    {
        var before = File.GetLastWriteTimeUtc(_path);

        using (var file = TagLib.File.Create(_path))
        {
            file.Tag.Title = title;
            file.Save();
        }

        // The tags fit the file's padding, so its size never moves, and Windows' clock ticks only
        // every 15.6 ms: set a time later than the last write's, as a real rewrite would have.
        File.SetLastWriteTimeUtc(_path, before.AddMinutes(1));
    }
}
