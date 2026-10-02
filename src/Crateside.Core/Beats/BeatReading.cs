using System.Text.Json.Serialization;

namespace Crateside.Core.Beats;

/// <summary>What ffmpeg's <c>beatdetect</c> reads on one instrumental: the whole file, and each 30 s of it.</summary>
public sealed record BeatReading
{
    /// <summary>The windows' spread, in percent, at which the tempo reads as drifting (#75).</summary>
    public const double DriftsAt = 0.1;

    private const double SameTempoClass = 0.06;
    private const int MinWindows = 3;

    /// <summary>The whole file's tempo, or null when the filter reported none.</summary>
    public double? Bpm { get; init; }

    public double? Confidence { get; init; }

    /// <summary>The filter's own drift figure, as a percentage; null from a build without it, or where it could not tell.</summary>
    public double? TempoSpread { get; init; }

    /// <summary>Each consecutive full 30 s window's tempo, from its beat interval.</summary>
    public IReadOnlyList<double> Windows { get; init; } = [];

    /// <summary>The windows' interquartile range in their majority octave, as a percentage of their median.</summary>
    [JsonIgnore]
    public double? Drift => Spread(Windows);

    [JsonIgnore]
    public bool Drifts => Drift >= DriftsAt;

    /// <summary>The whole file's tempo when it holds steady, which a single warp marker can stand for (#77).</summary>
    [JsonIgnore]
    public double? SteadyBpm => this is { Bpm: > 0 and var bpm, Drifts: false } ? bpm : null;

    /// <summary>Null under three windows in one tempo and octave; octave-flipped reads are not exact multiples.</summary>
    public static double? Spread(IReadOnlyList<double> windows)
    {
        if (windows.Count < MinWindows)
            return null;

        var median = Percentile([.. windows.Order()], 0.5);
        var folded = windows
            .Select(bpm =>
                (Bpm: bpm, Folded: bpm * Math.Pow(2, -Math.Round(Math.Log2(bpm / median))))
            )
            .ToArray();
        var reference = Percentile([.. folded.Select(window => window.Folded).Order()], 0.5);

        var inClass = folded
            .Where(window => Math.Abs(window.Folded - reference) / reference <= SameTempoClass)
            .Select(window =>
                (window.Bpm, Octave: (int)Math.Round(Math.Log2(window.Bpm / reference)))
            )
            .ToArray();

        if (inClass.Length is 0)
            return null;

        var majority = inClass
            .GroupBy(window => window.Octave)
            .OrderByDescending(octave => octave.Count())
            .ThenBy(octave => octave.Key)
            .First()
            .Key;

        double[] kept =
        [
            .. inClass
                .Where(window => window.Octave == majority)
                .Select(window => window.Bpm)
                .Order(),
        ];

        return kept.Length < MinWindows
            ? null
            : (Percentile(kept, 0.75) - Percentile(kept, 0.25)) / Percentile(kept, 0.5) * 100;
    }

    /// <summary>Linear between the closest ranks, as numpy's default.</summary>
    private static double Percentile(double[] sorted, double fraction)
    {
        var position = fraction * (sorted.Length - 1);
        var below = (int)Math.Floor(position);
        var above = Math.Min(below + 1, sorted.Length - 1);

        return sorted[below] + (sorted[above] - sorted[below]) * (position - below);
    }
}
