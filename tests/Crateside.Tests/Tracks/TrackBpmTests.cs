using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Ledger;
using Crateside.Core.Tempo;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>Which BPM a row shows: a finished warp's, else crateside's reading, else the ledger's estimate (ADR 0049).</summary>
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

    private static readonly BeatReading FullMix = new()
    {
        Bpm = 139.3,
        Windows = [139.3, 139.3, 139.3],
    };

    [Fact]
    public void A_track_with_no_stems_shows_the_full_mix_reading_marked_as_such()
    {
        var row = Row() with { FullMixBeats = FullMix };
        var shown = TempoFormat.Format(row.Bpm!);

        Assert.Equal((TempoSource.Reading, 139.3), (row.Bpm?.Source, row.Bpm?.Value));
        Assert.Equal("~139", shown.Text);
        Assert.Contains("from the full mix", shown.Note);
    }

    [Fact]
    public void The_full_mix_reading_outranks_the_ledgers_estimate_only_until_a_stem_is_read()
    {
        var provisional = Row() with { FullMixBeats = FullMix };
        var stems = Row(new TrackSample { Label = "Instrumental - Full", Beats = FullMix }) with
        {
            FullMixBeats = FullMix,
        };

        Assert.True(provisional.Bpm?.IsFromFullMix);
        Assert.False(stems.Bpm?.IsFromFullMix);
        Assert.DoesNotContain("full mix", TempoFormat.Format(stems.Bpm!).Note);
    }

    [Fact]
    public void The_full_mix_reading_feeds_the_display_and_nothing_a_stem_reading_does()
    {
        var row = Row() with { FullMixBeats = FullMix with { Windows = [100, 139, 170] } };

        Assert.Null(row.Beats);
        Assert.False(row.TempoDrifts);
        Assert.Null(TrackTempo.StarterBpm(row.Beats));
        Assert.False(row.Bpm!.IsVouched);
    }

    private static TrackRow Row(params TrackSample[] samples) =>
        new()
        {
            TrackKey = "aaaaaaaaaaa",
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
