using Crateside.Core.Ableton;
using Crateside.Core.Beats;

namespace Crateside.Core.Tempo;

/// <summary>A BPM as crateside shows it: the nominal value, where it came from, and the raw figures behind it (ADR 0049).</summary>
public sealed record TrackTempo
{
    /// <summary>The reading's window spread, in percent, at or under which it is good to the hundredth.</summary>
    public const double VouchedSpread = 0.02;

    private const double SnapWithin = 0.003;

    // A guard against snapping a warp to a different tempo; the snapped warps in #91 sit at most 0.017 off.
    private const double WarpSnapsWithin = 0.05;

    public required TempoSource Source { get; init; }

    /// <summary>The nominal tempo: what the track was made at when the evidence says so, else as measured.</summary>
    public required double Value { get; init; }

    public WarpTempo? Warp { get; init; }

    public BeatReading? Reading { get; init; }

    /// <summary>keytag's estimate.</summary>
    public double? Estimate { get; init; }

    /// <summary>The source's own figure, never snapped.</summary>
    public double Measured =>
        this switch
        {
            { Source: TempoSource.Warp, Warp: { } warp } => warp.Bpm,
            { Source: TempoSource.Reading, Reading.Bpm: { } read } => read,
            { Source: TempoSource.Estimate, Estimate: { } estimate } => estimate,
            _ => Value,
        };

    public bool Drifts =>
        this
            is { Source: TempoSource.Warp, Warp.Shape: TempoShape.Drifting }
                or { Source: TempoSource.Reading, Reading.Drifts: true };

    /// <summary>Known to the hundredth: any warp that holds, a typed figure, or a reading whose windows agree tightly.</summary>
    public bool IsVouched =>
        !Drifts
        && this
            is { Source: TempoSource.Warp or TempoSource.Typed }
                or { Source: TempoSource.Reading, Reading.Drift: <= VouchedSpread };

    /// <summary>A warp with two or more stable tempos.</summary>
    public bool ChangesTempo => this is { Source: TempoSource.Warp, Warp.Stable.Count: > 1 };

    /// <summary>A warp with one stable tempo and a ramp or drifting stretch besides.</summary>
    public bool PartChanges =>
        this
            is {
                Source: TempoSource.Warp,
                Warp: { Shape: TempoShape.MostlySteady, Stable.Count: 1 }
            };

    /// <summary>The source's windows in the octave of <see cref="Value"/>: a warp's four bars, a reading's 30 s, less its off-tempo ones.</summary>
    public IReadOnlyList<TempoWindow> Windows =>
        this switch
        {
            { Source: TempoSource.Warp, Warp.Windows: var windows } => windows,
            { Source: TempoSource.Reading, Reading.Windows: var windows } => Folded(
                BeatReading.Steady(windows)
            ),
            _ => [],
        };

    /// <summary>The slowest and fastest window of a drifting tempo.</summary>
    public (double Low, double High)? Range =>
        (Drifts, Windows, Warp) switch
        {
            (false, _, _) => null,
            (true, { Count: > 0 } windows, _) => (
                windows.Min(window => window.Bpm),
                windows.Max(window => window.Bpm)
            ),
            (true, _, { Low: var low, High: var high }) when Source is TempoSource.Warp => (
                low,
                high
            ),
            _ => null,
        };

    /// <summary>The headline tempo: the warp, else crateside's reading, else keytag's estimate; null with none of them.</summary>
    public static TrackTempo? Of(WarpTempo? warp, BeatReading? reading, double? estimate) =>
        (warp, reading, estimate) switch
        {
            ({ } held, _, _) => new()
            {
                Source = TempoSource.Warp,
                Value =
                    held.Shape is TempoShape.Drifting
                        ? held.Bpm
                        : Nominal(held.Bpm, reading, isWarp: true),
                Warp = held,
                Reading = reading,
                Estimate = estimate,
            },
            (null, { Bpm: > 0 and var read }, _) => new()
            {
                Source = TempoSource.Reading,
                Value = reading.Drifts ? read : Nominal(read, reading, isWarp: false),
                Reading = reading,
                Estimate = estimate,
            },
            (null, _, > 0 and var guess) => new()
            {
                Source = TempoSource.Estimate,
                Value = guess,
                Reading = reading,
                Estimate = guess,
            },
            _ => null,
        };

    public static TrackTempo Typed(double bpm) => new() { Source = TempoSource.Typed, Value = bpm };

    /// <summary>The tempo a starter warp's single marker is written at; null when the reading drifts or there is none (ADR 0048).</summary>
    public static double? StarterBpm(BeatReading? reading) =>
        Of(null, reading, null) is { Source: TempoSource.Reading, Drifts: false, Value: var bpm }
            ? bpm
            : null;

    /// <summary>This tempo as one source alone would give it, for showing the sources side by side; null when that source has nothing.</summary>
    public TrackTempo? From(TempoSource source) =>
        source switch
        {
            _ when source == Source => this,
            TempoSource.Warp => Warp is null ? null : Of(Warp, Reading, null),
            TempoSource.Reading => Of(null, Reading, null),
            TempoSource.Estimate => Of(null, null, Estimate),
            _ => null,
        };

    /// <summary>The same tempo shifted by <paramref name="semitones"/> without warping, as Re-Pitch plays it.</summary>
    public TrackTempo Shifted(int semitones) =>
        this with
        {
            Value = Pairing.RePitch.ArrangementBpm(Value, semitones),
        };

    private static double Nominal(double measured, BeatReading? reading, bool isWarp) =>
        reading is { Bpm: { } read, Drift: <= VouchedSpread }
        && Grid(read) is var grid
        && Math.Abs(read - grid) <= SnapWithin
        && Math.Abs(measured - grid) <= (isWarp ? WarpSnapsWithin : SnapWithin)
            ? grid
            : measured;

    /// <summary>The nearest whole or half number.</summary>
    private static double Grid(double bpm) =>
        Math.Round(bpm * 2, MidpointRounding.AwayFromZero) / 2;

    private TempoWindow[] Folded(IReadOnlyList<(int Index, double Bpm)> windows)
    {
        if (windows.Count is 0)
            return [];

        var median = windows.Select(window => window.Bpm).Order().ElementAt(windows.Count / 2);
        var octave = Math.Pow(2, Math.Round(Math.Log2(Value / median)));

        return
        [
            .. windows.Select(window => new TempoWindow(
                window.Index * BeatReading.WindowSeconds,
                (window.Index + 1) * BeatReading.WindowSeconds,
                window.Bpm * octave
            )),
        ];
    }
}

public enum TempoSource
{
    /// <summary>The user's warp, the instrumental's where there is one (ADR 0038).</summary>
    Warp,

    /// <summary>crateside's whole-file beatdetect reading of the instrumental.</summary>
    Reading,

    /// <summary>keytag's estimate, shown only while there is neither of the others.</summary>
    Estimate,

    /// <summary>A figure the user typed into Tools.</summary>
    Typed,
}
