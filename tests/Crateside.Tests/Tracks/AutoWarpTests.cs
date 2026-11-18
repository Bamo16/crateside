using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>The UI's "Auto warp": a row where any stem's warp is a starter.</summary>
public sealed class AutoWarpTests
{
    [Fact]
    public void A_row_is_flagged_when_any_stem_carries_an_auto_warp()
    {
        var row = Row(Sample(autoWarp: false), Sample(autoWarp: true));

        Assert.True(row.AutoWarp);
        Assert.True(row.Matches(TrackFilter.AutoWarp));
        Assert.Equal([row], new[] { row }.Narrow(TrackFilter.AutoWarp));
    }

    [Fact]
    public void A_row_whose_stems_carry_no_auto_warp_is_not()
    {
        var row = Row(Sample(autoWarp: false), Sample(autoWarp: false));

        Assert.False(row.AutoWarp);
        Assert.Empty(new[] { row }.Narrow(TrackFilter.AutoWarp));
    }

    [Fact]
    public void A_row_with_no_samples_is_not()
    {
        Assert.False(Row().AutoWarp);
    }

    private static TrackRow Row(params TrackSample[] samples) =>
        new()
        {
            TrackKey = "aaaaaaaaaaa",
            Title = "Track one",
            Samples = samples,
        };

    private static TrackSample Sample(bool autoWarp) =>
        new() { Label = "Vocal - Full", AutoWarp = autoWarp };
}
