using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Crateside.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crateside.Tests;

public sealed class BuildFetcherTests : IDisposable
{
    private const string Entry = "ffmpeg-windows/ffmpeg.exe";

    private readonly string _folder = Directory.CreateTempSubdirectory("crateside-fetch").FullName;

    private string Tools => Path.Combine(_folder, "tools");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_build_is_downloaded_verified_and_kept_in_a_folder_of_its_own()
    {
        var (pin, zip) = Pin("the build");
        var downloader = new FakeDownloader(zip);

        var outcome = await Fetcher(downloader)
            .EnsureAsync(pin, TestContext.Current.CancellationToken);

        var ready = Assert.IsType<BuildFetchOutcome.Ready>(outcome);
        Assert.Equal(pin.ExecutableIn(Tools), ready.FileName);
        Assert.Equal("the build", File.ReadAllText(ready.FileName));
        Assert.Equal(
            [ready.FileName],
            Directory.EnumerateFiles(Tools, "*", SearchOption.AllDirectories)
        );
    }

    [Fact]
    public async Task A_build_already_there_is_not_downloaded_again()
    {
        var (pin, zip) = Pin("the build");
        var downloader = new FakeDownloader(zip);
        var fetcher = Fetcher(downloader);

        await fetcher.EnsureAsync(pin, TestContext.Current.CancellationToken);
        await fetcher.EnsureAsync(pin, TestContext.Current.CancellationToken);

        Assert.Equal(1, downloader.Downloads);
    }

    [Fact]
    public async Task A_download_that_is_not_the_pinned_archive_installs_nothing()
    {
        var (pin, _) = Pin("the build");

        var outcome = await Fetcher(new FakeDownloader(Zip("a different build")))
            .EnsureAsync(pin, TestContext.Current.CancellationToken);

        Assert.IsType<BuildFetchOutcome.Failed>(outcome);
        Assert.Empty(Directory.EnumerateFiles(Tools, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task An_executable_that_is_not_the_pinned_one_installs_nothing()
    {
        var (pin, zip) = Pin("the build");
        var wrong = pin with { ExecutableSha256 = Hash("something else") };

        var outcome = await Fetcher(new FakeDownloader(zip))
            .EnsureAsync(wrong, TestContext.Current.CancellationToken);

        Assert.IsType<BuildFetchOutcome.Failed>(outcome);
        Assert.Empty(Directory.EnumerateFiles(Tools, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_download_that_fails_installs_nothing()
    {
        var (pin, _) = Pin("the build");

        var outcome = await Fetcher(new FakeDownloader(null))
            .EnsureAsync(pin, TestContext.Current.CancellationToken);

        Assert.IsType<BuildFetchOutcome.Failed>(outcome);
        Assert.Empty(Directory.EnumerateFiles(Tools, "*", SearchOption.AllDirectories));
    }

    internal static (BuiltInBuild Pin, byte[] Zip) Pin(string content, string tag = "v1.0.0")
    {
        var zip = Zip(content);

        return (
            new BuiltInBuild(
                FfmpegFilter.Keydetect,
                tag,
                "build.zip",
                Convert.ToHexStringLower(SHA256.HashData(zip)),
                Entry,
                Hash(content)
            ),
            zip
        );
    }

    internal static byte[] Zip(string content)
    {
        using var memory = new MemoryStream();

        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = archive.CreateEntry(Entry).Open();

            writer.Write(Encoding.UTF8.GetBytes(content));
        }

        return memory.ToArray();
    }

    internal static string Hash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private BuildFetcher Fetcher(IBuildDownloader downloader) =>
        new(
            downloader,
            new CratesidePaths { ToolsFolder = Tools },
            NullLogger<BuildFetcher>.Instance
        );

    internal sealed class FakeDownloader(byte[]? zip) : IBuildDownloader
    {
        public int Downloads { get; private set; }

        public async Task DownloadAsync(Uri url, string destination, CancellationToken ct)
        {
            Downloads++;

            if (zip is null)
                throw new HttpRequestException("offline");

            await File.WriteAllBytesAsync(destination, zip, ct);
        }
    }
}
