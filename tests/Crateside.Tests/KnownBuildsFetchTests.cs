using Crateside.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crateside.Tests;

public sealed class KnownBuildsFetchTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("crateside-seed").FullName;
    private readonly FakeFfmpegBuilds _ffmpeg;

    public KnownBuildsFetchTests() => _ffmpeg = new(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private CratesidePaths Paths(string? keydetectSeed = null) =>
        new()
        {
            KnownBuildsFile = Path.Combine(_folder, "builds.json"),
            ToolsFolder = Path.Combine(_folder, "tools"),
            BeatdetectFfmpeg = string.Empty,
            KeydetectFfmpeg = keydetectSeed ?? string.Empty,
        };

    private KnownBuilds Builds(
        BuildFetcherTests.FakeDownloader downloader,
        BuiltInBuild? pin,
        string? keydetectSeed = null
    )
    {
        var paths = Paths(keydetectSeed);
        var fetcher = new BuildFetcher(
            downloader,
            paths,
            NullLogger<BuildFetcher>.Instance,
            filter => filter == FfmpegFilter.Keydetect ? pin : null
        );

        if (pin is not null)
            _ffmpeg.Know(pin.ExecutableIn(paths.ToolsFolder), FfmpegFilter.Keydetect, pin.Tag);

        return new KnownBuilds(_ffmpeg, paths, fetcher);
    }

    [Fact]
    public async Task A_filter_with_no_build_gets_the_built_in_one_fetched_and_pinned()
    {
        var (pin, zip) = BuildFetcherTests.Pin("v1 build");
        var downloader = new BuildFetcherTests.FakeDownloader(zip);
        var builds = Builds(downloader, pin);

        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, downloader.Downloads);
        Assert.Equal(
            pin.ExecutableIn(Path.Combine(_folder, "tools")),
            builds.Pinned(FfmpegFilter.Keydetect)?.Path
        );
    }

    [Fact]
    public async Task A_build_the_maintainer_pinned_is_used_and_nothing_is_downloaded()
    {
        var (pin, zip) = BuildFetcherTests.Pin("v1 build");
        var downloader = new BuildFetcherTests.FakeDownloader(zip);
        var builds = Builds(downloader, pin);
        var own = _ffmpeg.Add(FfmpegFilter.Keydetect, "1.3.0");

        await builds.AddAsync(FfmpegFilter.Keydetect, own, TestContext.Current.CancellationToken);
        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, downloader.Downloads);
        Assert.Equal(own, builds.Pinned(FfmpegFilter.Keydetect)?.Path);
        Assert.Single(builds.All(FfmpegFilter.Keydetect));
    }

    [Fact]
    public async Task An_executable_named_in_configuration_is_used_and_nothing_is_downloaded()
    {
        var (pin, zip) = BuildFetcherTests.Pin("v1 build");
        var downloader = new BuildFetcherTests.FakeDownloader(zip);
        var configured = _ffmpeg.Add(FfmpegFilter.Keydetect, "1.2.1");
        var builds = Builds(downloader, pin, keydetectSeed: configured);

        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, downloader.Downloads);
        Assert.Equal(configured, builds.Pinned(FfmpegFilter.Keydetect)?.Path);
    }

    [Fact]
    public async Task A_failed_fetch_leaves_the_filter_with_no_build_and_does_not_throw()
    {
        var (pin, _) = BuildFetcherTests.Pin("v1 build");
        var builds = Builds(new BuildFetcherTests.FakeDownloader(null), pin);

        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Empty(builds.All(FfmpegFilter.Keydetect));
        Assert.Null(builds.Pinned(FfmpegFilter.Keydetect));
    }

    [Fact]
    public async Task A_built_in_pin_an_older_crateside_fetched_moves_to_the_new_one_and_keeps_the_old_known()
    {
        var (first, firstZip) = BuildFetcherTests.Pin("v1 build", tag: "v1.0.0");
        var (second, secondZip) = BuildFetcherTests.Pin("v2 build", tag: "v2.0.0");

        await Builds(new BuildFetcherTests.FakeDownloader(firstZip), first)
            .SeedAsync(TestContext.Current.CancellationToken);

        var later = Builds(new BuildFetcherTests.FakeDownloader(secondZip), second);
        await later.SeedAsync(TestContext.Current.CancellationToken);

        var tools = Path.Combine(_folder, "tools");
        Assert.Equal(second.ExecutableIn(tools), later.Pinned(FfmpegFilter.Keydetect)?.Path);
        Assert.Equal(2, later.All(FfmpegFilter.Keydetect).Count);
        Assert.True(File.Exists(first.ExecutableIn(tools)));
    }
}
