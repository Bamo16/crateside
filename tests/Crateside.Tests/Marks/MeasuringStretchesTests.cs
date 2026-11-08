using AbleKit.Analysis;
using Crateside.Core.Marks;
using Crateside.Core.Warps;

namespace Crateside.Tests.Marks;

public sealed class MeasuringStretchesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public MeasuringStretchesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A 400 s file warped at 120 BPM from beat 0 at 0 s, so bar N starts at 2 (N - 1) s and the clip starts on beat 4.</summary>
    private WarpGroup Warped()
    {
        var path = Path.Combine(_root, "Song (Instrumental - Full).flac");

        File.WriteAllBytes(path, [1, 2, 3]);
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                [.. Enumerable.Repeat(0.5f, 44_100 * 2)],
                2,
                [],
                3
            ) with
            {
                HeadTable = [400 * 44_100],
            },
            Warp = new Warp
            {
                Markers = [new(0, 0), new(120, 240), new(120.015625, 240.03125)],
                TimeSignature = new(4, 4),
            },
            Clip = new Clip
            {
                LoopStart = 4,
                LoopEnd = 8,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        }.Write($"{path}.asd");

        return WarpGroup.Of([
            new StemWarpReader(new WrittenWarps(), new TransientOrigins(), new LiveAnalyses()).Read(
                path
            ),
        ]);
    }

    private static readonly WarpGroup Unwarped = new([], null);

    [Fact]
    public void Bars_become_seconds_of_the_source_through_the_warp_to_the_end_of_the_last_bar()
    {
        Assert.True(MeasuringStretches.TryRead("183–190", "why", Warped(), out var stretch));

        Assert.Equal(364, stretch.StartSeconds, 0.001);
        Assert.Equal(380, stretch.EndSeconds, 0.001);
        Assert.Equal("183–190", stretch.Typed);
        Assert.Equal("why", stretch.Reason);
    }

    [Fact]
    public void A_beat_in_a_bar_is_that_position_and_an_end_with_a_beat_stops_there()
    {
        Assert.True(MeasuringStretches.TryRead("183.3-190.2", "why", Warped(), out var stretch));

        Assert.Equal(365, stretch.StartSeconds, 0.001);
        Assert.Equal(378.5, stretch.EndSeconds, 0.001);
    }

    [Fact]
    public void Start_and_end_are_the_edges_of_the_file()
    {
        Assert.True(MeasuringStretches.TryRead("190 end", "why", Warped(), out var stretch));

        Assert.Equal(378, stretch.StartSeconds, 0.001);
        Assert.Equal(400, stretch.EndSeconds, 0.001);
    }

    [Theory]
    [InlineData("0-4")]
    [InlineData("190-183")]
    [InlineData("183.5-190")]
    [InlineData("seven")]
    [InlineData("")]
    public void What_is_not_a_range_in_the_warp_is_refused(string text) =>
        Assert.False(MeasuringStretches.TryRead(text, "why", Warped(), out _));

    [Fact]
    public void Without_a_warp_a_stretch_is_in_minutes_and_seconds()
    {
        Assert.True(MeasuringStretches.TryRead("2:22–3:39", "why", Unwarped, out var stretch));

        Assert.Equal(142, stretch.StartSeconds);
        Assert.Equal(219, stretch.EndSeconds);
    }

    [Fact]
    public void Without_a_warp_a_bar_and_beat_is_refused()
    {
        Assert.False(MeasuringStretches.TryRead("183.3–190.2", "why", Unwarped, out _));
    }

    [Fact]
    public void A_stretch_is_shown_as_the_bars_it_was_typed_in_with_a_warp_and_as_time_without()
    {
        Assert.True(MeasuringStretches.TryRead("183–190", "why", Warped(), out var stretch));
        Assert.True(MeasuringStretches.TryRead("183.3-190.2", "why", Warped(), out var inside));

        Assert.Equal("bars 183–190 (6:04–6:20)", MeasuringStretches.Describe(stretch, Warped()));
        Assert.Equal(
            "bars 183.3.1–190.2.1 (6:05–6:19)",
            MeasuringStretches.Describe(inside, Warped())
        );
        Assert.Equal("6:04–6:20", MeasuringStretches.Describe(stretch, Unwarped));
    }
}
