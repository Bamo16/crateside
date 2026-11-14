namespace Crateside.Core.Warps;

/// <summary>How many grid lines the warp view draws behind its waveforms.</summary>
public enum GridDensity
{
    Off,
    Wide,
    Medium,
    Narrow,
}

/// <summary>A grid line's weight: a bar line is the strongest, a subdivision the faintest.</summary>
public enum GridStrength
{
    Bar,
    Beat,
    Subdivision,
}

public static class GridLines
{
    // How wide the plot is taken to be, the same width the waveform's columns are drawn at.
    public const double PlotPixels = 832;

    public static double MinPixels(GridDensity density) =>
        density switch
        {
            GridDensity.Wide => 64,
            GridDensity.Medium => 36,
            GridDensity.Narrow => 20,
            _ => double.PositiveInfinity,
        };

    /// <summary>
    /// The lines between two times: the finest of sixteenth, eighth, beat and bar whose lines stay at least
    /// the density's pixels apart, and every second, fourth or eighth bar when even a bar is closer than that.
    /// </summary>
    public static IReadOnlyList<(double At, GridStrength Strength)> Between(
        WarpGroup group,
        double from,
        double to,
        GridDensity density
    )
    {
        if (
            density is GridDensity.Off
            || group.Lender is null
            || to <= from
            || group.BeatAt(from) is not { } first
            || group.BeatAt(to) is not { } last
        )
            return [];

        var perBar = group.Bars.BeatsPerBar;
        var unit = 4.0 / group.Bars.Denominator;
        var minPixels = MinPixels(density);
        var middle = (first + last) / 2;
        var step = 0.0;

        foreach (var candidate in new[] { unit / 4, unit / 2, unit, perBar })
        {
            if (PixelsApart(group, middle, candidate, from, to) >= minPixels)
            {
                step = candidate;
                break;
            }
        }

        for (var bars = 2; step is 0 && bars <= 1024; bars *= 2)
        {
            if (PixelsApart(group, middle, perBar * bars, from, to) >= minPixels)
                step = perBar * bars;
        }

        if (step is 0)
            return [];

        List<(double, GridStrength)> lines = [];

        for (var n = (long)Math.Ceiling(first / step - 1e-6); n * step <= last + 1e-6; n++)
        {
            var beat = n * step;

            if (group.SecondsAt(beat) is not { } at)
                continue;

            var strength =
                group.Bars.IsBarLine(beat) ? GridStrength.Bar
                : IsMultiple(beat, unit) ? GridStrength.Beat
                : GridStrength.Subdivision;

            lines.Add((at, strength));
        }

        return lines;
    }

    private static double PixelsApart(
        WarpGroup group,
        double beat,
        double step,
        double from,
        double to
    ) =>
        group.SecondsAt(beat) is { } a && group.SecondsAt(beat + step) is { } b
            ? (b - a) / (to - from) * PlotPixels
            : 0;

    private static bool IsMultiple(double beat, double unit)
    {
        var count = beat / unit;

        return Math.Abs(count - Math.Round(count)) < 1e-4;
    }
}
