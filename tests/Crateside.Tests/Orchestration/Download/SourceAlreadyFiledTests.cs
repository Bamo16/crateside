using Crateside.Core;
using Crateside.Core.Orchestration.Download;

namespace Crateside.Tests.Orchestration.Download;

public sealed class SourceAlreadyFiledTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("filed").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private CratesidePaths Paths =>
        new()
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };

    [Fact]
    public void A_source_already_in_staging_is_not_copied_again()
    {
        Directory.CreateDirectory(Paths.StagingFolder);
        File.WriteAllBytes(Path.Combine(Paths.StagingFolder, "Song.flac"), [1]);

        Assert.True(DownloadDrainer.IsFiled(Paths, "Song.flac"));
    }

    [Fact]
    public void A_source_filed_in_a_library_subfolder_is_not_copied_again()
    {
        var folder = Path.Combine(Paths.LibraryFolder, "Hip Hop");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Song.flac"), [1]);

        Assert.True(DownloadDrainer.IsFiled(Paths, "Song.flac"));
    }

    [Fact]
    public void A_source_that_is_nowhere_yet_is_copied() =>
        Assert.False(DownloadDrainer.IsFiled(Paths, "Song.flac"));
}
