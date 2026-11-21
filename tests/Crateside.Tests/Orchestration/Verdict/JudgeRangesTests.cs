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
        TimeSignature? signature = null,
        double seconds = 400
    )
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():n} (Instrumental - Full).flac");

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
                HeadTable = [(int)(seconds * 44_100)],
            },
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

    /// <summary>Beat 0 five seconds in, at 120 BPM: the file opens on beat -10, bar 1 is at 5 s.</summary>
    private WarpGroup BeforeBeat0() =>
        Group([new(5, 0), new(125, 240), new(125.015625, 240.03125)], 0);

    private static (double, double)? Seconds(BarRangeReading reading) =>
        reading is BarRangeReading.Read(var start, var end) ? (start, end) : null;

    [Fact]
    public void Bars_become_seconds_through_the_warp_counted_from_its_beat_0_not_the_clip_start()
    {
        var window = JudgeRanges.Window(Warped(), "33–49", "Chorus");

        Assert.Equal(
            new RunWindow(64, 96, "Chorus", WindowAsker.User) { Bars = new(33, 48) },
            window
        );
    }

    [Fact]
    public void The_end_is_where_the_selection_stops_so_65_66_is_bar_65_alone()
    {
        Assert.Equal((128d, 130d), Seconds(BarRanges.Read("65–66", Warped())));
    }

    [Fact]
    public void Live_s_status_bar_positions_are_read_on_both_sides()
    {
        // Start: 155.1.1  End: 156.3.1
        Assert.Equal((308d, 311d), Seconds(BarRanges.Read("155.1.1–156.3.1", Warped())));
        Assert.Equal((308d, 311d), Seconds(BarRanges.Read("155–156.3.1", Warped())));
        Assert.Equal((308.5, 310d), Seconds(BarRanges.Read("155.2–156", Warped())));
    }

    [Theory]
    [InlineData("65–65")]
    [InlineData("65.1.1-65.1.1")]
    public void A_range_that_ends_where_it_starts_is_too_short(string text) =>
        Assert.IsType<BarRangeReading.TooShort>(BarRanges.Read(text, Warped()));

    [Fact]
    public void A_sixteenth_is_the_shortest_range()
    {
        Assert.Equal((128d, 128.125), Seconds(BarRanges.Read("65.1.1–65.1.2", Warped())));
    }

    [Fact]
    public void Seconds_are_shown_back_as_the_positions_Live_would_show()
    {
        Assert.Equal("34–50", BarRanges.Show(Warped(), 66, 98));
        Assert.Equal("155–156.3.1", BarRanges.Show(Warped(), 308, 311));
        Assert.Equal("65–66", BarRanges.Show(Warped(), 128, 130));
    }

    [Fact]
    public void Persona_bar_106_is_where_the_maintainer_hears_the_modulation_in_Live()
    {
        var window = JudgeRanges.Window(Persona(), "106–107", null);

        Assert.NotNull(window);
        Assert.Equal(141.84, window.Start, 0.01);
        Assert.Equal("106–107", BarRanges.Show(Persona(), window.Start, window.End));
    }

    [Fact]
    public void Persona_1_to_106_ends_at_the_start_of_bar_106_and_starts_at_the_top_of_the_file()
    {
        var window = JudgeRanges.Window(Persona(), "1–106", null);

        Assert.NotNull(window);
        Assert.Equal(0, window.Start, 0.01);
        Assert.Equal(141.84, window.End, 0.01);
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
        var group = BeforeBeat0();

        Assert.Equal("-3.3.1–1", BarRanges.Show(group, 0, 5));
        Assert.Equal(
            new RunWindow(1, 7, null, WindowAsker.User) { Bars = new(-2, 1) },
            JudgeRanges.Window(group, "-2–2", null)
        );
    }

    [Fact]
    public void Minus_1_to_1_is_the_bar_before_beat_0()
    {
        Assert.Equal((3d, 5d), Seconds(BarRanges.Read("-1–1", BeforeBeat0())));
    }

    [Fact]
    public void A_bar_in_six_eight_is_three_of_the_warps_beats()
    {
        var group = Group([new(0, 0), new(120, 240), new(120.015625, 240.03125)], 0, new(6, 8));

        Assert.Equal(
            new RunWindow(3, 6, null, WindowAsker.User) { Bars = new(3, 4) },
            JudgeRanges.Window(group, "3–5", null)
        );
    }

    [Theory]
    [InlineData("33–49")]
    [InlineData("33-49")]
    [InlineData("33 49")]
    [InlineData(" 33 – 49 ")]
    [InlineData("33 - 49")]
    public void A_bar_range_is_read_whichever_way_it_is_separated(string text) =>
        Assert.Equal((64d, 96d), Seconds(BarRanges.Read(text, Warped())));

    [Theory]
    [InlineData("-4–2", 0, 7)]
    [InlineData("-4-2", 0, 7)]
    [InlineData("-4--2", 0, 1)]
    [InlineData("-4 -2", 0, 1)]
    [InlineData("1-3", 5, 9)]
    public void A_bar_range_may_start_or_end_before_beat_0(string text, double start, double end) =>
        Assert.Equal((start, end), Seconds(BarRanges.Read(text, BeforeBeat0())));

    [Theory]
    [InlineData("start-106", 0, 141.84)]
    [InlineData("106-end", 141.84, 219)]
    [InlineData("106-", 141.84, 219)]
    [InlineData("106–", 141.84, 219)]
    [InlineData("start end", 0, 219)]
    [InlineData("106-999", 141.84, 219)]
    public void A_bar_range_may_leave_either_end_open_and_a_bar_past_the_end_is_clamped(
        string text,
        double start,
        double end
    )
    {
        // Persona is 3:39 long; bar 106 is at 141.84 s.
        var group = Group(
            [new(0.3135, 2.5), new(0.3135 + 60 / 176.9953 / 32, 2.5 + 1.0 / 32)],
            132,
            null,
            219
        );

        var reading = BarRanges.Read(text, group);

        Assert.IsType<BarRangeReading.Read>(reading);
        Assert.Equal(start, ((BarRangeReading.Read)reading).Start, 0.01);
        Assert.Equal(end, ((BarRangeReading.Read)reading).End, 0.01);
    }

    [Theory]
    [InlineData("48–33")]
    [InlineData("0-4")]
    [InlineData("-2 0")]
    [InlineData("33")]
    [InlineData("chorus")]
    [InlineData("")]
    [InlineData("-105")]
    [InlineData("106 ")]
    [InlineData("end-105")]
    [InlineData("106-start")]
    [InlineData("33.5–40")]
    [InlineData("33.1.5–40")]
    [InlineData("999-1000")]
    public void A_range_that_is_not_bars_in_order_is_refused(string text) =>
        Assert.IsType<BarRangeReading.Unreadable>(BarRanges.Read(text, Warped()));

    [Fact]
    public void Bars_are_refused_without_a_warp() =>
        Assert.IsType<BarRangeReading.Unreadable>(BarRanges.Read("33–49", new WarpGroup([], null)));

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
            "bars 82–97",
            new RunWindow(112, 133, null, WindowAsker.User) { Bars = new(82, 96) }.Name
        );
        Assert.Equal("1:52–2:13", new RunWindow(112, 133, null, WindowAsker.Judge).Name);
        Assert.Equal("Chorus", new RunWindow(112, 133, "Chorus", WindowAsker.User).Name);
    }
}
