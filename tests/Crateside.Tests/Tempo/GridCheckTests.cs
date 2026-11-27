using AbleKit.Analysis;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

/// <summary>Transients against a beat grid: a tempo's fit, and a warp's drift and steps.</summary>
public sealed class GridCheckTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void Refining_finds_the_tempo_the_transients_were_played_at() =>
        Assert.Equal(128.02, GridCheck.Refine(Hits(128.02, seconds: 240), 128), 3);

    [Fact]
    public void The_first_beat_is_where_the_loud_transients_fall_not_where_the_most_do()
    {
        var transients = Enumerable
            .Range(0, 80)
            .Select(beat => new Transient((int)((0.55 + beat * 0.5) * SampleRate), 0.7f))
            .Concat(
                Enumerable
                    .Range(0, 40)
                    .Select(bar => new Transient((int)((0.3 + bar * 1.0) * SampleRate), 1f))
            )
            .ToArray();

        Assert.Equal(0.3, GridCheck.FirstBeat(transients, SampleRate, 120) ?? 0, 2);
    }

    [Fact]
    public void A_3_3_2_rhythm_loudest_off_the_beat_is_placed_on_the_beat_beatdetect_gives()
    {
        // omniboi – Sugar Glider: hits on 16ths 0, 3 and 6 of every two beats, 16th 3 the loudest.
        const double Bpm = 69.99941;
        const double FirstBeat = 0.4;
        var transients = Pattern(Bpm, FirstBeat, (0, 0.85f), (3, 1f), (6, 0.9f));

        Assert.Equal(
            FirstBeat - Sixteenth(Bpm),
            GridCheck.FirstBeat(transients, SampleRate, Bpm) ?? 0,
            2
        );
        Assert.Equal(
            FirstBeat,
            GridCheck.FirstBeat(transients, SampleRate, Bpm, near: FirstBeat - 0.024) ?? 0,
            2
        );
    }

    [Fact]
    public void Offbeat_8ths_as_loud_as_the_beats_are_placed_on_the_beat_beatdetect_gives()
    {
        // VaVa, Snail's House – Persona: the beat and the offbeat 8th, the offbeat a touch louder.
        const double Bpm = 176.9953;
        const double FirstBeat = 0.1;
        var transients = Pattern(Bpm, FirstBeat, (0, 0.95f), (2, 1f), (4, 0.95f), (6, 1f));

        Assert.Equal(
            FirstBeat + 2 * Sixteenth(Bpm),
            GridCheck.FirstBeat(transients, SampleRate, Bpm) ?? 0,
            2
        );
        Assert.Equal(
            FirstBeat,
            GridCheck.FirstBeat(transients, SampleRate, Bpm, near: FirstBeat + 0.011) ?? 0,
            2
        );
    }

    [Fact]
    public void Too_few_transients_place_no_first_beat() =>
        Assert.Null(
            GridCheck.FirstBeat(
                [.. Enumerable.Range(0, 31).Select(i => new Transient(i * 22_050, 1f))],
                44_100,
                120
            )
        );

    [Fact]
    public void Transients_on_the_warps_grid_sit_at_no_offset_and_do_not_drift()
    {
        var report = GridCheck.Against(Hits(120, seconds: 180, at: 0.5), Warp(120, start: 0.5));

        Assert.All(report.Windows, window => Assert.InRange(window.OffsetMs, -0.5, 0.5));
        Assert.Empty(report.Steps);
    }

    [Fact]
    public void Every_window_of_a_two_tempo_warp_reports_the_same_lateness_in_its_own_tempo()
    {
        Warp warp = new() { Markers = [new(0, 0), new(60, 120), new(60 + 60 * 60 / 150, 270)] };
        double[] times =
        [
            .. Hits(120, seconds: 60).Select(time => time + 0.010),
            .. Hits(150, seconds: 60).Select(time => 60 + time + 0.010),
        ];

        var report = GridCheck.Against(times, warp);

        Assert.True(report.Windows.Count >= 10);
        Assert.All(report.Windows, window => Assert.InRange(window.OffsetMs, 9, 11));
    }

    [Fact]
    public void A_warp_a_little_fast_shows_a_steady_drift_and_no_step()
    {
        // Against a grid at 120.02, hits at 120 land 10 ms later each minute.
        var report = GridCheck.Against(Hits(120, seconds: 240), Warp(120.02));

        Assert.Equal(10, report.DriftMsPerMinute, 0.5);
        Assert.Empty(report.Steps);
    }

    [Fact]
    public void A_section_pasted_late_is_one_step_of_its_size_where_it_happened()
    {
        var hits = Hits(101, seconds: 330)
            .Select(time => time > 281 ? time + 0.048 : time)
            .ToArray();

        var report = GridCheck.Against(hits, 101);

        var step = Assert.Single(report.Steps);
        Assert.Equal(48, step.Ms, 1);
        Assert.InRange(step.At, 270, 290);
        Assert.Equal(0, report.DriftMsPerMinute, 0.5);
    }

    [Fact]
    public void The_stronger_half_is_kept_in_seconds()
    {
        Transient[] transients =
        [
            new(44100, 0.9f),
            new(88200, 0.1f),
            new(132300, 0.8f),
            new(176400, 0.2f),
        ];

        Assert.Equal([1.0, 3.0], GridCheck.Strongest(transients, 44100));
    }

    /// <summary>A hit on every 8th note at <paramref name="bpm"/>, starting at <paramref name="at"/> seconds.</summary>
    private static double[] Hits(double bpm, double seconds, double at = 0) =>
        [.. Enumerable.Range(0, (int)(seconds * bpm / 30)).Select(i => at + i * 30 / bpm)];

    private static Warp Warp(double bpm, double start = 0) =>
        new() { Markers = [new(start, 0), new(start + 60 / bpm, 1)] };

    private static double Sixteenth(double bpm) => 15 / bpm;

    /// <summary>A minute of a two-beat pattern from <paramref name="firstBeat"/>: each hit a 16th of the pattern and its energy.</summary>
    private static Transient[] Pattern(
        double bpm,
        double firstBeat,
        params (int Sixteenth, float Energy)[] hits
    ) =>
        [
            .. Enumerable
                .Range(0, (int)(bpm / 2))
                .SelectMany(pattern =>
                    hits.Select(hit => new Transient(
                        (int)
                            Math.Round(
                                (firstBeat + (pattern * 8 + hit.Sixteenth) * Sixteenth(bpm))
                                    * SampleRate
                            ),
                        hit.Energy
                    ))
                ),
        ];
}
