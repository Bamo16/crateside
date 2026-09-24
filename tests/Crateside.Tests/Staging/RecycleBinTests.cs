using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>Removing a sample recoverably.</summary>
public class RecycleBinTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public RecycleBinTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string Sample(string name = "sample.flac")
    {
        var path = Path.Combine(_root, name);

        File.WriteAllText(path, "audio");

        return path;
    }

    [Fact]
    public void A_removed_file_stops_existing()
    {
        var path = Sample();

        RecycleBin.Remove(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Removing_a_path_that_is_not_there_is_not_a_failure()
    {
        RecycleBin.Remove(Path.Combine(_root, "never-existed.flac"));
    }

    [Fact]
    public void Removing_the_same_file_twice_is_not_a_failure()
    {
        var path = Sample();

        RecycleBin.Remove(path);
        RecycleBin.Remove(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_file_held_open_is_reported_rather_than_lost()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows locks an open file.");

        var path = Sample("held.flac");

        using var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.ThrowsAny<IOException>(() => RecycleBin.Remove(path));
        Assert.True(File.Exists(path));
    }
}
