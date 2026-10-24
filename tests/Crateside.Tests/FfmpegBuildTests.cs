using Crateside.Core;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests;

public sealed class FfmpegBuildTests
{
    [Theory]
    [InlineData("9.0-NoMercy-beatdetect-v1.1.0", "1.3.0", true)]
    [InlineData("1.2.0", "1.3.0", true)]
    [InlineData("unversioned", "1.3.0", true)]
    [InlineData("1.3.0", "1.3.0", false)]
    [InlineData("1.3.0", "1.2.0", false)]
    [InlineData("1.3.0", "unversioned", false)]
    [InlineData("1.10.0", "1.9.0", false)]
    public void A_build_is_older_by_the_filters_version_number(
        string version,
        string pinned,
        bool older
    ) =>
        Assert.Equal(
            older,
            new FfmpegBuild(version, "a", null).IsOlderThan(new FfmpegBuild(pinned, "b", null))
        );

    [Fact]
    public async Task Keydetect_reports_its_own_version_and_a_build_without_beatdetect_has_none()
    {
        var executable = @"V:\Repos\keydetect-harness\builds\ffmpeg-keydetect-1.2.1.exe";

        if (!File.Exists(executable))
        {
            Assert.Skip("The keydetect 1.2.1 build is not on this machine");

            return;
        }

        var keydetect = await FfmpegBuild.IdentifyAsync(
            new ProcessRunner(),
            executable,
            FfmpegFilter.Keydetect,
            TestContext.Current.CancellationToken
        );
        var beatdetect = await FfmpegBuild.IdentifyAsync(
            new ProcessRunner(),
            executable,
            FfmpegFilter.Beatdetect,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("1.2.1", keydetect?.Version);
        Assert.Null(beatdetect);
    }
}
