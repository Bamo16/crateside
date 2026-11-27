using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Orchestration;

namespace Crateside.Tests.Imports;

public sealed class ImportStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public ImportStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            ImportsFolder = Path.Combine(_directory, "imports"),
            WorkingFolder = Path.Combine(_directory, "working"),
        };

    private string Original(string name)
    {
        var path = Path.Combine(_directory, name);

        File.WriteAllText(path, "audio");

        return path;
    }

    [Fact]
    public void A_taken_file_keeps_its_original_name_under_the_key()
    {
        var stored = new ImportStore(Paths).Take(
            "file~aa",
            Original("1-01. Song.flac"),
            move: false
        );

        Assert.Equal(Path.Combine(Paths.ImportsFolder, "file~aa", "1-01. Song.flac"), stored);
        Assert.Equal("audio", File.ReadAllText(stored));
    }

    [Fact]
    public void Copying_leaves_the_original_and_moving_does_not()
    {
        var store = new ImportStore(Paths);
        var copied = Original("copy.flac");
        var moved = Original("move.flac");

        store.Take("file~aa", copied, move: false);
        store.Take("file~bb", moved, move: true);

        Assert.True(File.Exists(copied));
        Assert.False(File.Exists(moved));
    }

    [Fact]
    public void The_working_set_finds_an_import_as_the_tracks_source()
    {
        var stored = new ImportStore(Paths).Take("file~aa", Original("a.flac"), move: true);

        Assert.True(new WorkingSet(Paths).TryGetSource("file~aa", out var found));
        Assert.Equal(stored, found);
        Assert.False(new WorkingSet(Paths).TryGetSource("file~zz", out _));
    }

    [Fact]
    public void Discarding_a_working_set_leaves_the_import()
    {
        var stored = new ImportStore(Paths).Take("file~aa", Original("a.flac"), move: true);
        var set = new WorkingSet(Paths);

        set.Prepare("file~aa");
        set.Discard("file~aa");

        Assert.True(File.Exists(stored));
        Assert.True(set.TryGetSource("file~aa", out _));
    }
}
