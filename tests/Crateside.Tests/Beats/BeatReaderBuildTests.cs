using System.Security.Cryptography;
using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Beats;

public sealed class BeatReaderBuildTests : IDisposable
{
    private const string Commit = "1e57fecb82c7378c13361feb42f235be71d3f8be";

    private readonly string _folder = Directory.CreateTempSubdirectory("crateside-build").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_build_beside_its_build_json_is_identified_by_commit_and_hash()
    {
        var ffmpeg = new FakeFfmpegBuilds(_folder);
        var executable = ffmpeg.Add(FfmpegFilter.Beatdetect, "1.3.0");
        var sha256 = Sha256Of(executable);
        WriteBuildJson(executable, sha256);

        var build = await new BeatReader(ffmpeg, fileName: executable).RefreshBuildAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new FfmpegBuild("1.3.0", sha256, Commit), build);
        Assert.Equal("1.3.0-1e57fec", build?.Label);
    }

    [Fact]
    public async Task A_build_json_recording_another_hash_vouches_for_no_commit()
    {
        var ffmpeg = new FakeFfmpegBuilds(_folder);
        var executable = ffmpeg.Add(FfmpegFilter.Beatdetect, "1.3.0");
        WriteBuildJson(executable, new string('0', 64));

        var build = await new BeatReader(ffmpeg, fileName: executable).RefreshBuildAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Null(build?.Commit);
        Assert.Equal("1.3.0", build?.Label);
    }

    [Fact]
    public async Task The_version_is_the_one_the_filter_reports_not_the_ffmpegs()
    {
        var pinned = RealPaths.PinnedBuild(FfmpegFilter.Beatdetect);
        var runner = new ProcessRunner();

        var build = await FfmpegBuild.IdentifyAsync(
            runner,
            pinned.Path,
            FfmpegFilter.Beatdetect,
            TestContext.Current.CancellationToken
        );
        var banner = await runner.RunAsync(
            new ProcessRequest(pinned.Path, ["-hide_banner", "-version"]),
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(build);
        Assert.Matches(@"^\d+\.\d+\.\d+$", build.Version);
        Assert.DoesNotContain(
            $"ffmpeg version {build.Version} ",
            banner.StandardOutput,
            StringComparison.Ordinal
        );
    }

    private static string Sha256Of(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WriteBuildJson(string executable, string sha256) =>
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, "build.json"),
            $$"""{"commit":"{{Commit}}","sha256":"{{sha256}}"}"""
        );
}
