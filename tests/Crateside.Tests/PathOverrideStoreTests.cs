using Crateside.Core;
using Microsoft.Extensions.Configuration;

namespace Crateside.Tests;

public sealed class PathOverrideStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string FilePath => Path.Combine(_directory, "paths.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_missing_file_reads_as_nothing_changed()
    {
        var overrides = new PathOverrideStore(FilePath).Read();

        Assert.Null(overrides.StagingFolder);
        Assert.Null(overrides.LibraryFolder);
    }

    [Fact]
    public void Remembered_folders_read_back()
    {
        var store = new PathOverrideStore(FilePath);

        store.Remember(@"D:\In", @"D:\Done");

        Assert.Equal(
            new PathOverrides { StagingFolder = @"D:\In", LibraryFolder = @"D:\Done" },
            store.Read()
        );
    }

    [Fact]
    public void The_file_overrides_appsettings_when_layered_over_it()
    {
        new PathOverrideStore(FilePath).Remember(@"D:\In", @"D:\Done");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Paths:StagingFolder"] = @"P:\Old",
                    ["Paths:LibraryFolder"] = @"P:\OldLibrary",
                }
            )
            .AddJsonFile(FilePath, optional: true)
            .Build();

        var paths = configuration.GetSection(CratesidePaths.SectionName).Get<CratesidePaths>();

        Assert.Equal(@"D:\In", paths?.StagingFolder);
        Assert.Equal(@"D:\Done", paths?.LibraryFolder);
    }
}
