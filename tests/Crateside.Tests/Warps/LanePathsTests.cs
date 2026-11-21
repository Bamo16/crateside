using Crateside.Core.Tracks;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class LanePathsTests
{
    private const string Guide = @"D:\Staging\Song (Drums).flac";

    private static TrackRow Row(string? guide) =>
        new()
        {
            TrackKey = "aaaaaaaaaaa",
            Title = "Song",
            HasWarpGuide = guide is not null,
            WarpGuidePath = guide,
            Samples =
            [
                new TrackSample
                {
                    Label = "Vocal - Full",
                    Path = @"D:\Library\Song (Vocal - Full).flac",
                    SourceTrackKey = "aaaaaaaaaaa",
                },
                new TrackSample
                {
                    Label = "Instrumental - Full",
                    Path = @"D:\Library\Song (Instrumental - Full).flac",
                    SourceTrackKey = "aaaaaaaaaaa",
                },
            ],
        };

    [Fact]
    public void The_warp_guide_is_the_last_lane_while_the_stems_are_on_disk()
    {
        var paths = StemWarpReader.LanePaths(Row(Guide));

        Assert.Equal(3, paths.Count);
        Assert.Equal(Guide, paths[^1]);
    }

    [Fact]
    public void A_track_without_a_warp_guide_has_only_its_stems()
    {
        Assert.Equal(2, StemWarpReader.LanePaths(Row(null)).Count);
    }
}
