using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Ledger;
using Crateside.Core.Tempo;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>Which BPM a row shows: a finished warp's, else crateside's reading, else keytag's estimate (ADR 0049).</summary>
public sealed class TrackBpmTests
{
    private static readonly LedgerRun Verdict = new()
    {
        Timestamp = DateTimeOffset.UnixEpoch,
        Song = "song",
        Bpm = 87,
    };

    [Fact]
    public void A_row_with_no_warp_and_no_reading_shows_the_verdicts_estimate()
    {
        var row = Row(Sample("Vocal - Full"));

        Assert.Equal((TempoSource.Estimate, 87.0), (row.Bpm?.Source, row.Bpm?.Value));
        Assert.Null(row.Tempo);
    }

    [Fact]
    public void A_reading_outranks_the_verdicts_estimate()
    {
        var row = Row(
            Sample("Vocal - Full"),
            new TrackSample
            {
                Label = "Instrumental - Full",
                Beats = new BeatReading { Bpm = 174.02, Windows = [174.02, 174.02, 174.03] },
            }
        );

        Assert.Equal((TempoSource.Reading, 174.02), (row.Bpm?.Source, row.Bpm?.Value));
    }

    [Fact]
    public void A_warped_sample_gives_the_row_its_tempo()
    {
        var row = Row(Sample("Vocal - Full", 174.0007));

        Assert.Equal((TempoSource.Warp, 174.0007), (row.Bpm?.Source, row.Bpm?.Measured));
    }

    [Fact]
    public void The_instrumental_decides_when_the_stems_disagree()
    {
        var row = Row(Sample("Vocal - Full", 86.5), Sample("Instrumental - Full", 173.9));

        Assert.Equal(173.9, row.Bpm?.Value);
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
