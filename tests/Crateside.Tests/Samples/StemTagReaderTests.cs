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

    private void Title(string title)
    {
        using var file = TagLib.File.Create(_path);
        file.Tag.Title = title;
        file.Save();
    }
}
