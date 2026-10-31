using System.Globalization;
using System.Text.Json.Serialization;

namespace Crateside.Core.Beats;

/// <summary>What ffmpeg's <c>beatdetect</c> reads on one file, an upload's source or instrumental: the whole file, and each 30 s of it.</summary>
public sealed record BeatReading
{
    /// <summary>The windows' spread, in percent, at which the tempo reads as drifting (#75).</summary>
    public const double DriftsAt = 0.1;

    public const int WindowSeconds = 30;

    private const double SameTempoClass = 0.06;
    private const int MinWindows = 3;

    /// <summary>The whole file's tempo, or null when the filter reported none.</summary>
    public double? Bpm { get; init; }

    public double? Confidence { get; init; }

    /// <summary>The filter's own drift figure, as a percentage; null from a build without it, or where it could not tell.</summary>
    public double? TempoSpread { get; init; }

    /// <summary>Where the first beat falls, in ms from the start of the file; null from a build without it.</summary>
    public double? BeatOffsetMs { get; init; }

    /// <summary>Where the first bar's downbeat falls, in ms; null from a build without it.</summary>
    public double? DownbeatOffsetMs { get; init; }

    /// <summary>How sure the filter is of the downbeat; <see cref="Warps.StarterGrid.DownbeatSureAt"/> and up is the working threshold.</summary>
    public double? DownbeatConfidence { get; init; }

    public int? BeatsPerBar { get; init; }

    /// <summary>How far the tempo moves through the file, as the filter reports it.</summary>
    public double? TempoWander { get; init; }

    /// <summary>The tempos the filter considered, the octave alternatives among them.</summary>
    public IReadOnlyList<BeatCandidate> Candidates { get; init; } = [];

    /// <summary>The stretches over which the filter held one tempo.</summary>
    public IReadOnlyList<TempoSegment> Segments { get; init; } = [];

    /// <summary>Each consecutive full 30 s window's tempo, from its beat interval.</summary>
    public IReadOnlyList<double> Windows { get; init; } = [];

    /// <summary>The windows' interquartile range in their majority octave, as a percentage of their median.</summary>
    [JsonIgnore]
    public double? Drift => Spread(Windows);

    [JsonIgnore]
    public bool Drifts => Drift >= DriftsAt;

    /// <summary>Null under three windows in one tempo and octave; octave-flipped reads are not exact multiples.</summary>
    public static double? Spread(IReadOnlyList<double> windows)
    {
        if (Steady(windows) is not { Count: >= MinWindows } kept)
            return null;

        double[] sorted = [.. kept.Select(window => window.Bpm).Order()];

        return (Percentile(sorted, 0.75) - Percentile(sorted, 0.25))
            / Percentile(sorted, 0.5)
            * 100;
    }

    /// <summary>The windows in the majority tempo and octave, by position; empty under three windows.</summary>
    public static IReadOnlyList<(int Index, double Bpm)> Steady(IReadOnlyList<double> windows)
    {
        if (windows.Count < MinWindows)
            return [];

        var median = Percentile([.. windows.Order()], 0.5);
        var folded = windows
            .Select(
                (bpm, index) =>
                    (
                        Index: index,
                        Bpm: bpm,
                        Folded: bpm * Math.Pow(2, -Math.Round(Math.Log2(bpm / median)))
                    )
            )
            .ToArray();
        var reference = Percentile([.. folded.Select(window => window.Folded).Order()], 0.5);

        var inClass = folded
            .Where(window => Math.Abs(window.Folded - reference) / reference <= SameTempoClass)
            .Select(window =>
                (
                    window.Index,
                    window.Bpm,
                    Octave: (int)Math.Round(Math.Log2(window.Bpm / reference))
                )
            )
            .ToArray();

        if (inClass.Length is 0)
            return [];

        var majority = inClass
            .GroupBy(window => window.Octave)
            .OrderByDescending(octave => octave.Count())
            .ThenBy(octave => octave.Key)
            .First()
            .Key;

        return
        [
            .. inClass
                .Where(window => window.Octave == majority)
                .Select(window => (window.Index, window.Bpm)),
        ];
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

/// <summary>One tempo the filter considered: its score, and where the first beat falls under it.</summary>
public sealed record BeatCandidate(double Bpm, double Score, double OffsetMs)
{
    internal static BeatCandidate? TryParse(string text) =>
        Numbers(text) is [var bpm, var score, var offset] ? new(bpm, score, offset) : null;

    internal static double[] Numbers(string text) =>
        text.Split(':') is var parts
        && parts.All(part => double.TryParse(part, CultureInfo.InvariantCulture, out _))
            ? [.. parts.Select(part => double.Parse(part, CultureInfo.InvariantCulture))]
            : [];
}

/// <summary>A stretch of the file, in ms, over which the filter held one tempo.</summary>
public sealed record TempoSegment(double StartMs, double EndMs, double Bpm)
{
    internal static TempoSegment? TryParse(string text) =>
        BeatCandidate.Numbers(text) is [var start, var end, var bpm] ? new(start, end, bpm) : null;
}
