using System.Text.RegularExpressions;
using Crateside.Core;

namespace Crateside.Tests;

public sealed partial class BuiltInBuildsTests
{
    [Theory]
    [InlineData(FfmpegFilter.Keydetect)]
    [InlineData(FfmpegFilter.Beatdetect)]
    public void Each_filter_has_a_pin_with_a_release_tag_and_both_hashes(FfmpegFilter filter)
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux(),
            "No build for this system."
        );

        var pin = Assert.IsType<BuiltInBuild>(BuiltInBuilds.For(filter));

        Assert.Matches(Hash, pin.ArchiveSha256);
        Assert.Matches(Hash, pin.ExecutableSha256);
        Assert.Matches(@"^v\d+\.\d+\.\d+$", pin.Tag);
        Assert.StartsWith(
            $"https://forgejo.phillippepelzer.me/FiLL/ffmpeg-{FfmpegBuild.NameOf(filter)}/releases/download/{pin.Tag}/",
            pin.ArchiveUrl.ToString()
        );
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Hash { get; }
}
