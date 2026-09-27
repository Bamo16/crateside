using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>Which BPM a row shows: a finished warp's over keytag's estimate (ADR 0038).</summary>
public class TrackBpmTests
{
    private static readonly LedgerRun Verdict = new()
    {
        Timestamp = DateTimeOffset.UnixEpoch,
        Song = "song",
        Bpm = 87,
    };

    [Fact]
    public void A_row_with_no_warp_shows_the_verdicts_estimate()
    {
        var row = Row(Sample("Vocal - Full"));

        Assert.Equal(87, row.Bpm);
        Assert.Null(row.Tempo);
    }

    [Fact]
    public void A_warped_sample_gives_the_row_its_tempo()
    {
        var row = Row(Sample("Vocal - Full", 174.0007));

        Assert.Equal(174.0007, row.Bpm);
    }

    [Fact]
    public void The_instrumental_decides_when_the_stems_disagree()
    {
        var row = Row(Sample("Vocal - Full", 86.5), Sample("Instrumental - Full", 173.9));

        Assert.Equal(173.9, row.Bpm);
    }

    [Fact]
    public void The_note_says_where_the_bpm_came_from_and_whether_it_holds()
    {
        Assert.Equal("keytag's estimate", Row(Sample("Vocal - Full")).BpmNote);
        Assert.Equal("from your warp", Row(Sample("Vocal - Full", 174)).BpmNote);
        Assert.Equal(
            "from your warp: unsteady, 91–94",
            Row(
                new TrackSample
                {
                    Label = "Instrumental - Full",
                    Tempo = new WarpTempo(92.1, TempoShape.Unsteady, 90.8, 93.5, []),
                }
            ).BpmNote
        );
    }

    private static TrackRow Row(params TrackSample[] samples) =>
        new()
        {
            VideoId = "aaaaaaaaaaa",
            Title = "Track one",
            Verdict = Verdict,
            Samples = samples,
        };

    private static TrackSample Sample(string label, double? bpm = null) =>
        new()
        {
            Label = label,
            Tempo = bpm is { } steady
                ? new WarpTempo(steady, TempoShape.Steady, steady, steady, [])
                : null,
        };
}
