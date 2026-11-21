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
        var pinned = RealPaths.PinnedBuild(FfmpegFilter.Keydetect);
        var runner = new ProcessRunner();
        var filters = await runner.RunAsync(
            new ProcessRequest(pinned.Path, ["-hide_banner", "-filters"]),
            TestContext.Current.CancellationToken
        );

        Assert.SkipWhen(
            filters.StandardOutput.Contains(" beatdetect ", StringComparison.Ordinal),
            "The pinned keydetect build carries beatdetect too."
        );

        var keydetect = await IdentifyAsync(runner, pinned.Path, FfmpegFilter.Keydetect);
        var beatdetect = await IdentifyAsync(runner, pinned.Path, FfmpegFilter.Beatdetect);

        Assert.Matches(@"^\d+\.\d+\.\d+$", keydetect?.Version);
        Assert.Null(beatdetect);
    }

    private static Task<FfmpegBuild?> IdentifyAsync(
        ProcessRunner runner,
        string fileName,
        FfmpegFilter filter
    ) => FfmpegBuild.IdentifyAsync(runner, fileName, filter, TestContext.Current.CancellationToken);
}
