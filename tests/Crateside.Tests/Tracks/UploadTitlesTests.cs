using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

public sealed class UploadTitlesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public UploadTitlesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Tagged(string name, string? title)
    {
        var path = Path.Combine(_root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Title = title;
        file.Save();

        return path;
    }

    private static TrackRow Row(params TrackSample[] samples) =>
        new()
        {
            TrackKey = "main",
            Title = "Artist - Song",
            Samples = samples,
        };

    private static TrackSample Sample(string path, string upload) =>
        new()
        {
            Path = path,
            Label = "source",
            SourceTrackKey = upload,
        };

    [Fact]
    public void An_upload_is_named_by_the_title_its_files_carry()
    {
        var row = Row(
            Sample(Tagged("a.flac", "Song (Main Version)"), "main"),
            Sample(Tagged("b.flac", "Song (Instrumental)"), "added")
        );

        Assert.Equal("Song (Instrumental)", UploadTitles.Of(row, "added"));
        Assert.Equal("Song (Instrumental)", UploadTitles.Name(row, "added"));
    }

    [Fact]
    public void An_upload_with_no_title_is_named_by_its_video_id()
    {
        var row = Row(Sample(Tagged("a.flac", null), "main"));

        Assert.Null(UploadTitles.Of(row, "main"));
        Assert.Equal("main", UploadTitles.Name(row, "main"));
        Assert.Equal("other", UploadTitles.Name(row, "other"));
    }
}
