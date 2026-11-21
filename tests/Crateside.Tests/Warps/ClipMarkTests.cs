using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class ClipMarkTests
{
    [Fact]
    public void A_clip_that_starts_before_the_audio_has_no_marker_outside_edit_mode()
    {
        var group = Group(markerBeat: 1, clipStartBeat: 0);

        Assert.Null(group.ClipMarkFor(editing: false));
    }

    [Fact]
    public void A_clip_that_starts_before_the_audio_is_marked_unset_at_the_markers_bar_in_edit_mode()
    {
        var group = Group(markerBeat: 1, clipStartBeat: 0);

        var mark = Assert.NotNull(group.ClipMarkFor(editing: true));

        Assert.True(mark.Unset);
        Assert.Equal(1, mark.Bar);
        Assert.Equal(0, mark.Seconds);
    }

    [Fact]
    public void An_unset_marker_stands_at_the_bar_of_a_marker_that_sits_in_a_later_bar()
    {
        var group = Group(markerBeat: 5, clipStartBeat: 0, first: 2);

        var mark = Assert.NotNull(group.ClipMarkFor(editing: true));

        Assert.Equal(2, mark.Bar);
        Assert.Equal(group.SecondsAt(4)!.Value, mark.Seconds, precision: 6);
    }

    [Fact]
    public void A_clip_start_on_the_audio_is_marked_where_it_is_and_set()
    {
        var group = Group(markerBeat: 1, clipStartBeat: 4);

        var mark = Assert.NotNull(group.ClipMarkFor(editing: true));

        Assert.False(mark.Unset);
        Assert.Equal(2, mark.Bar);
        Assert.Equal(group.SecondsAt(4)!.Value, mark.Seconds, precision: 6);
    }

    [Fact]
    public void A_stems_clip_runs_from_the_mixs_clip_start_to_the_end_of_its_file()
    {
        var group = Group(markerBeat: 1, clipStartBeat: 4);

        var (start, end) = group.ClipSpan(group.Stems[0]);

        Assert.Equal(group.SecondsAt(4)!.Value, start, precision: 6);
        Assert.Equal(20, end);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    public void An_added_sources_clip_starts_at_the_mixs_clip_start_unless_its_file_starts_later(
        double shift,
        double expectedStartAtLeast
    )
    {
        var group = Group(markerBeat: 1, clipStartBeat: 4);
        var added = new StemWarp
        {
            Path = "Song (Official).flac",
            Owner = new WarpOwner.None(),
            Seconds = 10,
        };

        var (start, end) = group.ClipSpan(added, shift);

        Assert.Equal(
            Math.Max(group.SecondsAt(4)!.Value, expectedStartAtLeast),
            start,
            precision: 6
        );
        Assert.Equal(10 + shift, end);
    }

    private static WarpGroup Group(double markerBeat, double clipStartBeat, double first = 0.1245)
    {
        const double beat = 60 / 102.0;
        var stem = new StemWarp
        {
            Path = "Song (Instrumental - Full).flac",
            Owner = new WarpOwner.None(),
            Seconds = 20,
            Sidecar = new AnalysisFile
            {
                Audio = AudioAnalysis.FromSamples([.. Enumerable.Repeat(0.5f, 44_100)], 1, [], 3),
                Warp = new Warp
                {
                    Markers =
                    [
                        new(first, markerBeat),
                        new(first + 4 * beat, markerBeat + 4),
                        new(first + 4 * beat + 1 / 64.0, markerBeat + 4 + 1 / 64.0 * 102 / 60),
                    ],
                },
                Clip = new Clip
                {
                    LoopStart = clipStartBeat,
                    LoopEnd = clipStartBeat + 16,
                    LoopOn = false,
                },
                IsDefaultClipSaved = true,
            },
        };

        return new WarpGroup([stem], stem);
    }
}
