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

    /// <summary>A warp at 120 BPM whose clip starts on beat 4, so the clip starts at 2 s and bar 1 at 0 s.</summary>
    private WarpGroup Warped() => Group([new(0, 0), new(120, 240), new(120.015625, 240.03125)], 4);

    /// <summary>
    /// VaVa, Snail's House – <i>Persona</i> (<c>kbV8MOB6mWY</c>) as its warp stands: one marker,
    /// beat 2.5 at 0.3135 s, at 176.9953 BPM, and the default clip from beat 132, 44.213 s.
    /// </summary>
    private WarpGroup Persona()
    {
        const double secondsPerBeat = 60 / 176.9953;
        WarpMarker marker = new(0.3135, 2.5);
        WarpMarker hidden = new(marker.Seconds + secondsPerBeat / 32, marker.Beat + 1.0 / 32);

        return Group([marker, hidden], 132);
    }

    private WarpGroup Group(
        IReadOnlyList<WarpMarker> markers,
        double clipStart,
        TimeSignature? signature = null
    )
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():n} (Instrumental - Full).flac");

        File.WriteAllBytes(path, [1, 2, 3]);
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples([.. Enumerable.Repeat(0.5f, 44_100 * 2)], 2, [], 3),
            Warp = new Warp { Markers = markers, TimeSignature = signature ?? new(4, 4) },
            Clip = new Clip
            {
                LoopStart = clipStart,
                LoopEnd = clipStart + 4,
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
    public void Bars_become_seconds_through_the_warp_counted_from_its_beat_0_not_the_clip_start()
    {
        var window = JudgeRanges.Window(Warped(), new BarSpan(33, 48), "Chorus");

        Assert.Equal(
            new RunWindow(64, 96, "Chorus", WindowAsker.User) { Bars = new(33, 48) },
            window
        );
    }

    [Fact]
    public void Seconds_the_judge_asked_for_are_drawn_as_the_bars_they_cover() =>
        Assert.Equal(new BarSpan(34, 49), JudgeRanges.BarsOf(Warped(), 66, 98));

    [Fact]
    public void Persona_bar_106_is_where_the_maintainer_hears_the_modulation_in_Live()
    {
        var window = JudgeRanges.Window(Persona(), new BarSpan(106, 106), null);

        Assert.NotNull(window);
        Assert.Equal(141.84, window.Start, 0.01);
        Assert.Equal(new BarSpan(106, 106), JudgeRanges.BarsOf(Persona(), 142, 143));
    }

    [Fact]
    public void Persona_1_to_106_starts_at_the_top_of_the_file_and_not_at_the_clip_start()
    {
        var window = JudgeRanges.Window(Persona(), new BarSpan(1, 106), null);

        Assert.NotNull(window);
        Assert.Equal(0, window.Start, 0.01);
        Assert.Equal(143.2, window.End, 0.1);
    }

    [Fact]
    public void Persona_clip_start_is_bar_34_as_Live_shows_it()
    {
        var group = Persona();

        Assert.Equal(34, group.Bars.BarAt(group.ClipStartBeat));
        Assert.Equal("34", group.Bars.Position(group.ClipStartBeat));
    }

    [Fact]
    public void Audio_before_beat_0_is_in_negative_bars_with_no_bar_0()
    {
        // Beat 0 five seconds in, at 120 BPM: the file opens on beat -10.
        var group = Group([new(5, 0), new(125, 240), new(125.015625, 240.03125)], 0);

        Assert.Equal(new BarSpan(-3, -1), JudgeRanges.BarsOf(group, 0, 5));
        Assert.Equal(
            new RunWindow(1, 7, null, WindowAsker.User) { Bars = new(-2, 1) },
            JudgeRanges.Window(group, new BarSpan(-2, 1), null)
        );
    }

    [Fact]
    public void A_bar_in_six_eight_is_three_of_the_warps_beats()
    {
        var group = Group([new(0, 0), new(120, 240), new(120.015625, 240.03125)], 0, new(6, 8));

        Assert.Equal(
            new RunWindow(3, 6, null, WindowAsker.User) { Bars = new(3, 4) },
            JudgeRanges.Window(group, new BarSpan(3, 4), null)
        );
    }

    [Theory]
    [InlineData("33–48")]
    [InlineData("33-48")]
    [InlineData("33 48")]
    [InlineData(" 33 – 48 ")]
    [InlineData("33 - 48")]
    public void A_bar_range_is_read_whichever_way_it_is_separated(string text)
    {
        Assert.True(JudgeRanges.TryParseBars(text, out var bars));
        Assert.Equal(new BarSpan(33, 48), bars);
    }

    [Theory]
    [InlineData("-4–2", -4, 2)]
    [InlineData("-4-2", -4, 2)]
    [InlineData("-4--2", -4, -2)]
    [InlineData("-4 -2", -4, -2)]
    [InlineData("1-106", 1, 106)]
    public void A_bar_range_may_start_or_end_before_beat_0(string text, int first, int last)
    {
        Assert.True(JudgeRanges.TryParseBars(text, out var bars));
        Assert.Equal(new BarSpan(first, last), bars);
    }

    [Theory]
    [InlineData("48–33")]
    [InlineData("0-4")]
    [InlineData("-2 0")]
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
