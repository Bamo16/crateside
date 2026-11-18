using AbleKit.Analysis;
using Crateside.Core.Warps;
using Crateside.Tests.Tempo;

namespace Crateside.Tests.Warps;

public sealed class WarpEditTests
{
    private static AnalysisFile Track => SteadyTrack.Analysis(120, 120, seconds: 60);

    private static double[] Beats(AnalysisFile file) => [.. file.Warp.Markers.Select(m => m.Beat)];

    private static double[] Seconds(AnalysisFile file) =>
        [.. file.Warp.Markers.Select(m => m.Seconds)];

    [Fact]
    public void Shifting_the_grid_keeps_every_marker_in_the_audio_and_lowers_its_beat()
    {
        var edited = WarpEdit.Apply(Track, new WarpDraft().Later(4));

        Assert.Equal(Seconds(Track), Seconds(edited));
        Assert.Equal(Beats(Track).Select(beat => beat - 4), Beats(edited));
        Assert.True(edited.IsDefaultClipSaved);
    }

    [Fact]
    public void Nudging_moves_marker_times_and_the_clip_follows_the_audio()
    {
        var edited = WarpEdit.Apply(Track, new WarpDraft().Nudged(4));

        Assert.Equal(Seconds(Track).Select(s => s + 0.004), Seconds(edited));
        Assert.Equal(Beats(Track), Beats(edited));
        Assert.Equal(Track.Clip.LoopEnd, edited.Clip.LoopEnd, 6);
    }

    [Fact]
    public void Halving_about_the_clip_start_keeps_its_seconds_and_lands_a_marker_on_a_half_beat()
    {
        var draft = new WarpDraft().Scaled(0.5, 0);
        var edited = WarpEdit.Apply(Track, draft);

        Assert.Equal(Seconds(Track), Seconds(edited));
        Assert.Equal(0.5, edited.Warp.Markers[1].Beat, 9);
        Assert.Equal(0, edited.Warp.Markers[0].Beat, 9);
    }

    [Fact]
    public void Doubling_after_halving_takes_the_step_back()
    {
        var draft = new WarpDraft().Scaled(0.5, 0).Scaled(2, 0);

        Assert.Empty(draft.Tempo);
        Assert.Equal(Beats(Track), Beats(WarpEdit.Apply(Track, draft)));
    }

    [Fact]
    public void Edits_commute()
    {
        var one = new WarpDraft().Later(4).Nudged(5).Scaled(0.5, 0);
        var other = new WarpDraft().Scaled(0.5, 0).Nudged(5).Later(4);

        var (a, b) = (WarpEdit.Apply(Track, one), WarpEdit.Apply(Track, other));

        Assert.Equal(Beats(a), Beats(b));
        Assert.Equal(Seconds(a), Seconds(b));
        Assert.Equal(a.Clip, b.Clip);
    }

    [Fact]
    public void The_clip_start_is_a_whole_bar_and_follows_the_grid()
    {
        var draft = new WarpDraft().WithClipStart(3);
        var edited = WarpEdit.Apply(Track, draft);

        Assert.Equal(8, edited.Clip.LoopStart);
        Assert.Equal(8, WarpEdit.ClipStartBeat(Track, draft));
        Assert.Equal(Track.Clip.LoopEnd, edited.Clip.LoopEnd, 6);
    }

    [Fact]
    public void A_file_without_a_warp_is_returned_unchanged()
    {
        var bare = Track with { Warp = new Warp { Markers = [] } };

        Assert.Same(bare, WarpEdit.Apply(bare, new WarpDraft().Later(1)));
    }
}
