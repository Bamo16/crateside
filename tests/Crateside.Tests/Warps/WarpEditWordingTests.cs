using AbleKit.Analysis;
using Crateside.Core.Warps;
using Crateside.Tests.Tempo;

namespace Crateside.Tests.Warps;

public sealed class WarpEditWordingTests
{
    [Fact]
    public void Changes_names_each_edit_a_draft_holds()
    {
        var draft = new WarpDraft().Later(2).Nudged(4);

        var words = WarpEditWording.Changes(draft);

        Assert.Contains("bar two beats later", words);
        Assert.Contains("+4 ms", words);
    }

    [Fact]
    public void Saves_to_lists_what_is_written_kept_and_lent()
    {
        var lines = WarpEditWording.SavesTo(["Drums", "Vocal"], ["Bass"], ["Drums - Kick"]);

        Assert.Equal(
            [
                "Saves to Drums and Vocal",
                "Bass keeps its own warp",
                "then lends it again to Drums - Kick",
            ],
            lines
        );
    }

    private static AnalysisFile Track => SteadyTrack.Analysis(120, 120, seconds: 60);

    private static EditReadout BarOf(WarpDraft draft, bool autoWarp = false) =>
        WarpEditWording.Bar(Track, WarpEdit.Apply(Track, draft), draft, seconds: 60, autoWarp);

    [Fact]
    public void An_untouched_bar_reads_as_saved_and_unchanged()
    {
        Assert.Equal(
            new EditReadout("bar 1 starts on the marker's beat 1, as saved", false),
            BarOf(new WarpDraft())
        );
        Assert.Equal(
            new EditReadout("bar 1 starts on the marker's beat 1, where crateside put it", false),
            BarOf(new WarpDraft(), autoWarp: true)
        );
    }

    [Fact]
    public void A_bar_moved_a_beat_says_which_beat_the_marker_is_on_and_which_it_was()
    {
        Assert.Equal(
            new EditReadout("bar 1 starts on the marker's beat 2 · was beat 1", true),
            BarOf(new WarpDraft().Later(1))
        );
        Assert.Equal(
            new EditReadout(
                "bar 1 starts on the marker's beat 2 · crateside put it on beat 1",
                true
            ),
            BarOf(new WarpDraft().Later(1), autoWarp: true)
        );
    }

    [Fact]
    public void A_bar_moved_a_whole_bar_wraps_back_to_beat_one_but_is_still_a_change()
    {
        Assert.Equal(
            new EditReadout("bar 1 starts on the marker's beat 1 · was beat 1", true),
            BarOf(new WarpDraft().Later(4))
        );
    }

    [Fact]
    public void A_marker_pushed_more_than_a_bar_off_says_where_it_falls_instead()
    {
        var readout = BarOf(new WarpDraft().Later(5));

        Assert.True(readout.Changed);
        Assert.StartsWith("the marker falls on bar", readout.Text);
        Assert.EndsWith(" · it was on bar 1, beat 1", readout.Text);
    }

    [Fact]
    public void A_halved_tempo_reads_from_what_it_was_to_what_it_is_now()
    {
        var draft = new WarpDraft().Scaled(0.5, 0);
        var drafted = WarpEdit.Apply(Track, draft);

        Assert.Equal(
            new EditReadout("120 → 60", true),
            WarpEditWording.Tempo(Track, drafted, draft)
        );
        Assert.Equal("60 was 120", WarpEditWording.DraftTempo(Track, drafted, draft));
    }

    [Fact]
    public void An_unchanged_tempo_reads_as_the_saved_figure_with_no_draft_figure()
    {
        var draft = new WarpDraft().Later(1);
        var drafted = WarpEdit.Apply(Track, draft);

        Assert.Equal(new EditReadout("120", false), WarpEditWording.Tempo(Track, drafted, draft));
        Assert.Null(WarpEditWording.DraftTempo(Track, drafted, draft));
    }

    [Fact]
    public void A_moved_clip_start_names_its_bar_and_the_bar_it_was()
    {
        Assert.Equal(
            new EditReadout("clip start · bar 3 · was bar 1", true),
            WarpEditWording.ClipStart(Track, new WarpDraft().WithClipStart(3))
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void A_clip_start_left_where_it_is_reads_unchanged(int? bar)
    {
        Assert.Equal(
            new EditReadout("clip start · bar 1", false),
            WarpEditWording.ClipStart(Track, new WarpDraft { ClipStartBar = bar })
        );
    }

    [Fact]
    public void Several_stems_keeping_their_own_warps_are_said_in_the_plural()
    {
        var lines = WarpEditWording.SavesTo(["Drums"], ["Bass", "Vocal"], []);

        Assert.Equal(["Saves to Drums", "Bass and Vocal keep their own warps"], lines);
    }

    [Fact]
    public void A_nudge_earlier_uses_a_real_minus_and_names_the_saved_warp_when_not_a_auto_warp()
    {
        Assert.Equal(
            new EditReadout("−2.5 ms from the saved warp", true),
            WarpEditWording.Nudge(new WarpDraft().Nudged(-2.5), autoWarp: false)
        );
        Assert.Equal(
            new EditReadout("as saved", false),
            WarpEditWording.Nudge(new WarpDraft(), autoWarp: false)
        );
    }

    [Fact]
    public void Nudge_reads_as_on_disk_until_it_is_moved()
    {
        Assert.False(WarpEditWording.Nudge(new WarpDraft(), autoWarp: true).Changed);
        Assert.Equal(
            "+3 ms from where crateside put it",
            WarpEditWording.Nudge(new WarpDraft().Nudged(3), autoWarp: true).Text
        );
    }
}
