using Crateside.Core.Ableton;

namespace Crateside.Tests.Ableton;

/// <summary>ADR 0038's rule on warps built from stretches of bars at a tempo.</summary>
public class WarpTempoTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void A_single_marker_is_steady_at_its_exact_tempo()
    {
        var tempo = Of(Warp(1, (200, 128.02)));

        Assert.Equal(TempoShape.Steady, tempo.Shape);
        Assert.Equal(128.02, tempo.Bpm, 6);
    }

    [Fact]
    public void The_bpm_is_the_stable_tempo_that_holds_for_the_most_bars()
    {
        var tempo = Of(Warp(1, (90, 180), (24, 132), (80, 180)));

        Assert.Equal(TempoShape.SteadyWithChanges, tempo.Shape);
        Assert.Equal(180, tempo.Bpm, 6);
        Assert.Equal([180, 132], tempo.Stable.Select(stable => Math.Round(stable.Bpm, 3)));
        Assert.Equal(24, tempo.Stable[1].Bars);
    }

    [Fact]
    public void Without_a_run_of_eight_bars_the_warp_is_unsteady_and_keeps_its_range()
    {
        var tempo = Of(Warp(1, Alternating(90, 94, 40)));

        Assert.Equal(TempoShape.Unsteady, tempo.Shape);
        Assert.Equal(92, tempo.Bpm, 6);
        Assert.Equal((90, 94), (Math.Round(tempo.Low, 6), Math.Round(tempo.High, 6)));
    }

    [Fact]
    public void A_run_of_silence_is_not_a_stable_tempo()
    {
        var segments = Alternating(168, 172, 40).Append((30, 131.9)).ToArray();
        var silentFrom = Seconds(Alternating(168, 172, 40));

        var tempo = Of(Warp(1, segments) with { Overview = Silent(from: silentFrom) });

        Assert.Equal(TempoShape.Unsteady, tempo.Shape);
        Assert.Equal(170, tempo.Bpm, 6);
    }

    [Fact]
    public void A_run_takes_the_tempo_most_of_its_bars_hold()
    {
        // Two bars a hair off at the start fit the same line within 3 ms but would pull a fitted
        // tempo away from the 174 every other bar holds.
        var tempo = Of(Warp(1, (1, 174.3), (1, 173.7), (100, 174)));

        Assert.Equal(174, tempo.Bpm, 6);
    }

    [Fact]
    public void Only_the_default_clip_counts()
    {
        // A 145 intro before verse 1, which starts at bar 9.
        var tempo = Of(Warp(9, (8, 145), (100, 105)));

        Assert.Equal(TempoShape.Steady, tempo.Shape);
        Assert.Equal(105, tempo.Bpm, 6);
    }

    [Fact]
    public void A_warp_with_no_saved_default_clip_has_no_tempo()
    {
        Assert.Null(WarpTempo.Of(Warp(1, (100, 120)) with { DefaultClip = null }, SampleRate));
    }

    [Theory]
    [InlineData(0, 256, 0.5f)]
    [InlineData(256, 512, 0.25f)]
    [InlineData(384, 10_000, 0f)]
    [InlineData(10_000, 20_000, 0f)]
    public void An_overview_reads_the_loudest_bin_and_nothing_past_its_end(
        long from,
        long to,
        float expected
    )
    {
        var overview = new SampleOverview(128, [0.5f, 0.1f, 0.25f]);

        Assert.Equal(expected, overview.PeakBetween(from, to));
    }

    private static WarpTempo Of(AbletonWarp warp) =>
        WarpTempo.Of(warp, SampleRate) ?? throw new InvalidOperationException("no tempo");

    /// <summary>Markers at each change of tempo, and a default clip from bar <paramref name="startBar"/> to the end.</summary>
    private static AbletonWarp Warp(int startBar, params (int Bars, double Bpm)[] segments)
    {
        List<WarpMarker> markers = [new(0, 0)];
        double seconds = 0,
            beat = 0;

        foreach (var (bars, bpm) in segments)
        {
            seconds += bars * 4 * 60 / bpm;
            beat += bars * 4;
            markers.Add(new(seconds, beat));
        }

        var last = segments[^1].Bpm;
        markers.Add(new(seconds + 60 / last / 32, beat + 1.0 / 32));

        return new AbletonWarp(
            true,
            WarpMode.Complex,
            4,
            4,
            markers,
            new DefaultClip((startBar - 1) * 4, beat)
        );
    }

    private static (int Bars, double Bpm)[] Alternating(double low, double high, int bars) =>
        [.. Enumerable.Range(0, bars / 2).Select(i => (2, i % 2 == 0 ? low : high))];

    private static double Seconds((int Bars, double Bpm)[] segments) =>
        segments.Sum(segment => segment.Bars * 4 * 60 / segment.Bpm);

    private static SampleOverview Silent(double from)
    {
        const int perBin = 128;
        var silentBin = (int)(from * SampleRate / perBin);

        return new SampleOverview(
            perBin,
            [.. Enumerable.Range(0, silentBin + 100_000).Select(bin => bin < silentBin ? 0.5f : 0f)]
        );
    }
}
