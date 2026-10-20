using Crateside.Core.Ableton;
using Crateside.Core.Beats;

namespace Crateside.Core.Tempo;

/// <summary>A BPM as crateside shows it: the value, where it came from, and the figures behind it (ADR 0049, 0050).</summary>
public sealed record TrackTempo
{
    /// <summary>The reading's window spread, in percent, at or under which it is good to the hundredth.</summary>
    public const double VouchedSpread = 0.02;

    /// <summary>How far a tight reading may sit from a warp before the warp is called off its tempo.</summary>
    public const double ReadingDisagreesAt = 0.01;

    // Beyond this the reading is a different octave or tempo, not a hair off.
    private const double SameTempoWithin = 0.5;

    public required TempoSource Source { get; init; }

    /// <summary>The tempo as its source gives it, shifted if the track is.</summary>
    public required double Value { get; init; }

    public WarpTempo? Warp { get; init; }

    public BeatReading? Reading { get; init; }

    /// <summary>The ledger's estimate.</summary>
    public double? Estimate { get; init; }

    /// <summary>The source's own figure, before any shift.</summary>
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

    /// <summary>Known to the hundredth: a steady warp whose grid holds against the audio, any other warp that holds, a typed figure, or a reading whose windows agree tightly.</summary>
    public bool IsVouched =>
        !Drifts
        && this switch
        {
            { Source: TempoSource.Warp, Warp: { Shape: TempoShape.Steady, Grid: var grid } } => grid
                is { Holds: true },
            { Source: TempoSource.Warp or TempoSource.Typed } => true,
            { Source: TempoSource.Reading, Reading.Drift: <= VouchedSpread } => true,
            _ => false,
        };

    /// <summary>A steady warp that sits off the tempo the audio holds, and the tempo it should be at (#96).</summary>
    public TempoOff? Off =>
        this switch
        {
            {
                Source: TempoSource.Warp,
                Warp: { Shape: TempoShape.Steady, Grid: { TrueBpm: { } bpm } grid }
            } => new(bpm, grid.DriftMs),
            {
                Source: TempoSource.Warp,
                Warp: { Shape: TempoShape.Steady, Grid.Holds: true, Bpm: var warp },
                Reading: { Bpm: { } read, Drift: <= VouchedSpread }
            } when Math.Abs(read - warp) is >= ReadingDisagreesAt and < SameTempoWithin => new(
                read,
                null
            ),
            _ => null,
        };

    /// <summary>A steady warp the audio gives no verdict on: a step in its grid, too few transients, or no tempo that holds.</summary>
    public WarpGrid? Unconfirmed =>
        this is { Source: TempoSource.Warp, Warp: { Shape: TempoShape.Steady, Grid: { } grid } }
        && grid is { Holds: false, TrueBpm: null }
            ? grid
            : null;

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

    /// <summary>The headline tempo: the warp, else crateside's reading, else the ledger's estimate; null with none of them.</summary>
    public static TrackTempo? Of(WarpTempo? warp, BeatReading? reading, double? estimate) =>
        (warp, reading, estimate) switch
        {
            ({ } held, _, _) => new()
            {
                Source = TempoSource.Warp,
                Value = held.Bpm,
                Warp = held,
                Reading = reading,
                Estimate = estimate,
            },
            (null, { Bpm: > 0 and var read }, _) => new()
            {
                Source = TempoSource.Reading,
                Value = read,
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

    /// <summary>The tempo a starter warp's single marker is written at, as read; null when the reading drifts or there is none (ADR 0048).</summary>
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

/// <param name="Bpm">The tempo the warp should be at.</param>
/// <param name="DriftMs">What the warp drifts over the track, positive when it runs fast; null where a reading, not the grid, says so.</param>
public sealed record TempoOff(double Bpm, double? DriftMs);

public enum TempoSource
{
    /// <summary>The user's warp, the instrumental's where there is one (ADR 0038).</summary>
    Warp,

    /// <summary>crateside's whole-file beatdetect reading of the instrumental.</summary>
    Reading,

    /// <summary>The ledger's estimate, shown only while there is neither of the others.</summary>
    Estimate,

    /// <summary>A figure the user typed into Tools.</summary>
    Typed,
}
