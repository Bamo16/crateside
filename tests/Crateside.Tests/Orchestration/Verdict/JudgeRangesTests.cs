using AbleKit.Analysis;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Warps;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class JudgeRangesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public JudgeRangesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A warp at 120 BPM whose clip starts on beat 4, so bar 1 is at 2 s.</summary>
    private WarpGroup Warped()
    {
        var path = Path.Combine(_root, "Song (Instrumental - Full).flac");

        File.WriteAllBytes(path, [1, 2, 3]);
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples([.. Enumerable.Repeat(0.5f, 44_100 * 2)], 2, [], 3),
            Warp = new Warp { Markers = [new(0, 0), new(120, 240), new(120.015625, 240.03125)] },
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

    [Fact]
    public void Bars_become_seconds_through_the_warp_from_the_clip_start_as_Live_counts_them()
    {
        var window = JudgeRanges.Window(Warped(), new BarSpan(33, 48), "Chorus");

        Assert.Equal(
            new RunWindow(66, 98, "Chorus", WindowAsker.User) { Bars = new(33, 48) },
            window
        );
    }

    [Fact]
    public void Seconds_the_judge_asked_for_are_drawn_as_the_bars_they_cover() =>
        Assert.Equal(new BarSpan(33, 48), JudgeRanges.BarsOf(Warped(), 66, 98));

    [Theory]
    [InlineData("33–48")]
    [InlineData("33-48")]
    [InlineData("33 48")]
    [InlineData(" 33 – 48 ")]
    public void A_bar_range_is_read_whichever_way_it_is_separated(string text)
    {
        Assert.True(JudgeRanges.TryParseBars(text, out var bars));
        Assert.Equal(new BarSpan(33, 48), bars);
    }

    [Theory]
    [InlineData("48–33")]
    [InlineData("0-4")]
    [InlineData("33")]
    [InlineData("chorus")]
    [InlineData("")]
    public void A_range_that_is_not_bars_in_order_is_refused(string text) =>
        Assert.False(JudgeRanges.TryParseBars(text, out _));

    [Theory]
    [InlineData("0:45–1:07", 45, 67)]
    [InlineData("0:45-1:07", 45, 67)]
    [InlineData("45 67", 45, 67)]
    [InlineData("1:02.5–1:34", 62.5, 94)]
    public void A_time_range_is_read_in_minutes_and_seconds_or_seconds(
        string text,
        double start,
        double end
    )
    {
        Assert.True(JudgeRanges.TryParseTimes(text, out var from, out var to));
        Assert.Equal((start, end), (from, to));
    }

    [Theory]
    [InlineData("1:07–0:45")]
    [InlineData("0:75–1:00")]
    [InlineData("soon")]
    public void A_time_range_that_is_not_a_stretch_is_refused(string text) =>
        Assert.False(JudgeRanges.TryParseTimes(text, out _, out _));

    [Fact]
    public void A_window_without_a_label_is_named_by_its_bars_or_else_its_time()
    {
        Assert.Equal(
            "bars 82–96",
            new RunWindow(112, 133, null, WindowAsker.User) { Bars = new(82, 96) }.Name
        );
        Assert.Equal("1:52–2:13", new RunWindow(112, 133, null, WindowAsker.Judge).Name);
        Assert.Equal("Chorus", new RunWindow(112, 133, "Chorus", WindowAsker.User).Name);
    }
}
