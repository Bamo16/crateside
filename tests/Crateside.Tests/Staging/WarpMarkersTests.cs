using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

public class WarpMarkersTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    [Fact]
    public void The_sidecar_appends_to_the_full_filename()
    {
        // song.flac pairs with song.flac.asd, not song.asd.
        Assert.Equal(
            @"C:\Stems\Yes - Owner of a Lonely Heart (Drums).flac.asd",
            WarpMarkers.SidecarPathFor(@"C:\Stems\Yes - Owner of a Lonely Heart (Drums).flac")
        );
    }

    [Fact]
    public void Warped_and_unwarped_stems_both_exist_in_the_real_staging_folder()
    {
        Assert.SkipUnless(Directory.Exists(StagingRoot), $"{StagingRoot} not present.");

        var stems = Directory.GetFiles(StagingRoot, "*.flac");
        var warped = stems.Where(WarpMarkers.IsWarped).ToArray();

        // Warping is per-stem and ragged — that raggedness is what the board exists to show,
        // so a run where every stem is warped or none is would make this assertion vacuous.
        Assert.NotEmpty(stems);
        Assert.NotEmpty(warped);
        Assert.NotEqual(stems.Length, warped.Length);
    }

    [Fact]
    public void Every_asd_sidecar_belongs_to_a_stem_that_exists()
    {
        Assert.SkipUnless(Directory.Exists(StagingRoot), $"{StagingRoot} not present.");

        var orphans = Directory
            .GetFiles(StagingRoot, "*.asd")
            .Where(asd => !File.Exists(asd[..^".asd".Length]))
            .ToArray();

        Assert.Empty(orphans);
    }
}
