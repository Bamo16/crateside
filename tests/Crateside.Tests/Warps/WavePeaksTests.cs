using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class WavePeaksTests
{
    private const int SampleRate = 44_100;
    private const int Frames = SampleRate * 4;

    [Fact]
    public void A_column_is_one_of_Lives_bins_while_the_track_fits_under_the_cap()
    {
        var peaks = Peaks(Mono(0.5f), cap: 65_536);

        Assert.Equal(1379, peaks.Columns);
        Assert.Equal(128.0 / SampleRate, peaks.SecondsPerColumn);
        Assert.Equal(1, peaks.Channels);
        Assert.Equal(peaks.Columns * 2, peaks.Bytes.Length);
    }

    [Fact]
    public void A_longer_track_takes_a_whole_number_of_bins_to_the_column()
    {
        var peaks = Peaks(Mono(0.5f), cap: 500);

        Assert.Equal(460, peaks.Columns);
        Assert.Equal(3 * 128.0 / SampleRate, peaks.SecondsPerColumn);
    }

    [Fact]
    public void A_stretch_is_drawn_as_the_loudest_of_the_columns_each_slice_covers()
    {
        var samples = Mono(0.25f);
        samples.AsSpan(Frames - 2_000).Fill(0.75f);

        var drawn = Peaks(samples, cap: 65_536).Envelope(0, 4, across: 2);

        var channel = Assert.Single(drawn);
        Assert.Equal(0.25, channel[0].Max, 2);
        Assert.Equal(0.75, channel[1].Max, 2);
    }

    [Fact]
    public void A_sample_below_zero_is_the_columns_low()
    {
        var samples = Mono(0.5f);
        samples.AsSpan(0, 1_000).Fill(-0.5f);

        var drawn = Peaks(samples, cap: 65_536).Envelope(0, 4, across: 4);

        Assert.Equal(-0.5, drawn[0][0].Min, 2);
        Assert.Equal(0, drawn[0][3].Min, 2);
    }

    [Fact]
    public void A_slice_past_the_tracks_end_is_silent()
    {
        var drawn = Peaks(Mono(0.5f), cap: 65_536).Envelope(3, 7, across: 2);

        Assert.Equal(0.5, drawn[0][0].Max, 2);
        Assert.Equal(0, drawn[0][1].Max, 2);
    }

    [Fact]
    public void Each_channel_keeps_its_own_columns()
    {
        var samples = new float[Frames * 2];

        for (var i = 0; i < Frames; i++)
            (samples[i * 2], samples[i * 2 + 1]) = (0.25f, 0.75f);

        var drawn = WavePeaks
            .Of(SampleOverview.FromSamples(samples, 2), SampleRate, cap: 65_536)
            .Envelope(0, 4, across: 1);

        Assert.Equal(2, drawn.Count);
        Assert.Equal(0.25, drawn[0][0].Max, 2);
        Assert.Equal(0.75, drawn[1][0].Max, 2);
    }

    [Fact]
    public void A_stem_without_an_overview_draws_nothing()
    {
        var peaks = WavePeaks.Of(null, SampleRate, cap: 65_536);

        Assert.Equal(0, peaks.Columns);
        Assert.Empty(peaks.Envelope(0, 4, across: 8));
    }

    private static float[] Mono(float level) => [.. Enumerable.Repeat(level, Frames)];

    private static WavePeaks Peaks(float[] samples, int cap) =>
        WavePeaks.Of(SampleOverview.FromSamples(samples, 1), SampleRate, cap);
}
