using Crateside.Core;

namespace Crateside.Tests;

public sealed class FolderBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FolderBrowser _browser = new();

    public FolderBrowserTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "beta", "inner"));
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        File.WriteAllText(Path.Combine(_root, "file.txt"), "not a folder");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_folder_lists_its_subfolders_by_name_and_no_files()
    {
        var listing = _browser.List(_root);

        Assert.Equal(["Alpha", "beta"], listing.Folders.Select(folder => folder.Name));
        Assert.Equal(_root, listing.Path);
        Assert.Equal(Path.GetDirectoryName(_root), listing.Parent);
    }

    [Fact]
    public void A_folder_that_is_gone_lists_its_nearest_existing_ancestor()
    {
        var listing = _browser.List(Path.Combine(_root, "beta", "moved away", "deeper"));

        Assert.Equal(Path.Combine(_root, "beta"), listing.Path);
        Assert.Equal(["inner"], listing.Folders.Select(folder => folder.Name));
    }

    [Fact]
    public void Nothing_to_start_from_lists_the_drives()
    {
        var listing = _browser.List(null);

        Assert.Equal(string.Empty, listing.Path);
        Assert.Null(listing.Parent);
        Assert.Contains(listing.Folders, drive => drive.Path == Path.GetPathRoot(_root));
    }

    [Fact]
    public void A_drive_root_goes_up_to_the_drives()
    {
        var listing = _browser.List(Path.GetPathRoot(_root));

        Assert.Equal(string.Empty, listing.Parent);
    }
}
