using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Beats;

public class BeatReaderBuildTests
{
    private const string HarnessBuilds = @"V:\Repos\beatdetect-harness\builds";

    [Fact]
    public async Task A_build_beside_its_build_json_is_identified_by_commit_and_hash()
    {
        if (
            !Directory.Exists(HarnessBuilds)
            || Directory
                .EnumerateDirectories(HarnessBuilds)
                .Select(folder => Path.Combine(folder, "ffmpeg.exe"))
                .FirstOrDefault(File.Exists)
                is not { } executable
        )
        {
            Assert.Skip("No harness build on this machine");

            return;
        }

        var reader = new BeatReader(new ProcessRunner(), fileName: executable);

        var build = await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(build);
        Assert.NotNull(build.Commit);
        Assert.Equal(64, build.Sha256.Length);
        Assert.EndsWith(build.Commit[..7], build.Label);
    }

    [Theory]
    [InlineData("1.2.0-563e4e4", "1.2.0")]
    [InlineData("1.3.0-1e57fec", "1.3.0")]
    public async Task The_version_is_the_one_the_filter_reports_not_the_ffmpegs(
        string folder,
        string version
    )
    {
        var executable = Path.Combine(HarnessBuilds, folder, "ffmpeg.exe");

        if (!File.Exists(executable))
        {
            Assert.Skip("No harness build on this machine");

            return;
        }

        var build = await FfmpegBuild.IdentifyAsync(
            new ProcessRunner(),
            executable,
            FfmpegFilter.Beatdetect,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(version, build?.Version);
    }
}
