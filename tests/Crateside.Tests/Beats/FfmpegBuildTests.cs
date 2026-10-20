using Crateside.Core.Beats;

namespace Crateside.Tests.Beats;

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
}
