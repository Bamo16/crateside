using Crateside.Core.SourceOffsets;

namespace Crateside.Tests.SourceOffsets;

public sealed class OffsetWordingTests
{
    private const int Rate = 44_100;
    private static readonly string NoBreak = ((char)0xA0).ToString();

    private static OffsetWindow[] Windows(int voting, int unmatched) =>
        [
            .. Enumerable
                .Range(0, voting)
                .Select(i => new OffsetWindow(i * 5, 19_470, 20, WindowState.Voting)),
            .. Enumerable
                .Range(0, unmatched)
                .Select(i => new OffsetWindow((voting + i) * 5, 0, 3, WindowState.Unmatched)),
        ];

    [Fact]
    public void An_aligned_line_gives_the_shift_in_milliseconds_and_samples_and_the_window_count()
    {
        var reading = new OffsetReading
        {
            Verdict = OffsetVerdict.Aligned,
            SampleRate = Rate,
            WindowSeconds = 5,
            Offset = 19_470,
            Windows = Windows(51, 1),
        };

        Assert.Equal(
            "Aligned to the mix: +441.5 ms (19" + NoBreak + "470 samples), 51 of 52 windows",
            OffsetWording.Line(reading)
        );
    }

    [Fact]
    public void An_early_start_reads_as_a_negative_shift()
    {
        var reading = new OffsetReading
        {
            Verdict = OffsetVerdict.Aligned,
            SampleRate = Rate,
            WindowSeconds = 5,
            Offset = -2_812,
            Windows = Windows(35, 4),
        };

        Assert.StartsWith(
            "Aligned to the mix: -63.8 ms (-2" + NoBreak + "812 samples)",
            OffsetWording.Line(reading)
        );
    }

    [Fact]
    public void An_edit_line_names_each_run_and_says_it_is_not_written()
    {
        var reading = new OffsetReading
        {
            Verdict = OffsetVerdict.Edit,
            SampleRate = Rate,
            WindowSeconds = 5,
            Runs = [new(0, 25, 0), new(25, 40, 132_300)],
        };

        var line = OffsetWording.Line(reading);

        Assert.Contains("0.00 s up to 25 s", line);
        Assert.Contains("+3.00 s from 25 s to 40 s", line);
        Assert.Contains("not written", line);
    }

    [Fact]
    public void A_drift_after_a_speed_retry_says_which_way_and_how_much_it_runs()
    {
        var reading = new OffsetReading
        {
            Verdict = OffsetVerdict.Drift,
            SampleRate = Rate,
            WindowSeconds = 5,
            SpeedFactor = "1000/1001",
            DriftPpm = -999.0,
        };

        Assert.Equal(
            "Runs 0.1% slow against the mix; use the audio release; not written",
            OffsetWording.Line(reading)
        );
    }

    [Fact]
    public void The_save_lists_each_source_with_its_shift_or_the_reason_it_is_left_out()
    {
        var file = new AddedSourceFile("a.flac", "key", "Official Acappella");
        var reading = new OffsetReading
        {
            Verdict = OffsetVerdict.Aligned,
            SampleRate = Rate,
            WindowSeconds = 5,
            Offset = 359_546,
        };
        var stamp = new FileStamp("a.flac", 1, DateTime.UnixEpoch);
        var record = new SourceOffsetRecord(
            "main",
            stamp,
            stamp,
            reading,
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(
            "Official Acappella: shifted by +8153.0 ms",
            OffsetWording.Saves(new AddedSourcePlan(file, null, null, record))
        );
        Assert.Equal(
            "Official Acappella: not written, has its own warp",
            OffsetWording.Saves(new AddedSourcePlan(file, AddedSkip.HasOwnWarp, null, record))
        );
        Assert.Equal(
            "Official Acappella: not written, cut differently from the mix",
            OffsetWording.Saves(new AddedSourcePlan(file, AddedSkip.Edit, null, record))
        );
    }

    [Fact]
    public void Spans_only_one_file_has_are_listed_apart_from_the_verdict()
    {
        var reading = new OffsetReading
        {
            Verdict = OffsetVerdict.Aligned,
            SampleRate = Rate,
            WindowSeconds = 5,
            Offset = 0,
            OnlyInSource = [new(75, 85)],
            OnlyInMix = [new(74.9, 84.9)],
        };

        Assert.Equal(
            ["only in this file: 75-85 s", "only in the mix: 74.9-84.9 s"],
            OffsetWording.Unmatched(reading)
        );
    }
}
