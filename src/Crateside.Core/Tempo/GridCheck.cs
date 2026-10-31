using AbleKit.Analysis;

namespace Crateside.Core.Tempo;

/// <summary>Where a track's transients land against a beat grid: how well a tempo fits, and where a warp drifts or steps.</summary>
public static class GridCheck
{
    public const double WindowSeconds = 10;

    /// <summary>How far the windows either side of a boundary must part, in ms, to count as a step.</summary>
    public const double StepMs = 15;

    private const int StepWindows = 3;
    private const int MinPerWindow = 4;
    private const int SixteenthsPerBeat = 4;
    private const int MinToPlace = 32;
    private const double LoudnessPower = 4;
    private const int SmoothingBins = 3;

    /// <summary>The louder half of the transients, in seconds: the quiet half is mostly ghost notes and bleed.</summary>
    public static double[] Strongest(IReadOnlyList<Transient> transients, int sampleRate)
    {
        if (transients.Count is 0)
            return [];

        var median = transients
            .Select(transient => transient.Energy)
            .Order()
            .ElementAt(transients.Count / 2);

        return
        [
            .. transients
                .Where(transient => transient.Energy >= median)
                .Select(transient => (double)transient.Position / sampleRate),
        ];
    }

    /// <summary>How tightly the transients sit on a 16th-note grid at <paramref name="bpm"/>, whatever its phase: 1 is every one on a line.</summary>
    public static double Fit(IReadOnlyList<double> times, double bpm) =>
        Concentration(times.Select(time => time * bpm / 60));

    /// <summary>The tempo within <paramref name="span"/> BPM of <paramref name="bpm"/> that the transients fit best, to 0.0005.</summary>
    public static double Refine(IReadOnlyList<double> times, double bpm, double span = 0.06)
    {
        const double Step = 0.0005;
        var steps = (int)Math.Round(span / Step);

        return Enumerable
            .Range(-steps, 2 * steps + 1)
            .Select(i => bpm + i * Step)
            .MaxBy(candidate => Fit(times, candidate));
    }

    /// <summary>
    /// Where the first beat falls at <paramref name="bpm"/>, in seconds: the transients folded onto one beat, louder to the
    /// fourth power, the peak taken within half a 16th of <paramref name="near"/> when given (ADR 0054); null when too few.
    /// </summary>
    public static double? FirstBeat(
        IReadOnlyList<Transient> transients,
        int sampleRate,
        double bpm,
        double? near = null
    )
    {
        if (transients.Count < MinToPlace)
            return null;

        var beatMs = 60_000 / bpm;
        var bins = (int)Math.Ceiling(beatMs);
        var weights = new double[bins];

        foreach (var transient in transients)
        {
            var ms = transient.Position * 1000.0 / sampleRate % beatMs;
            weights[(int)ms % bins] += Math.Pow(transient.Energy, LoudnessPower);
        }

        var candidates = near is { } nearSeconds
            ? Enumerable
                .Range(0, bins)
                .Where(bin =>
                    Circular(bin + 0.5 - nearSeconds * 1000, beatMs)
                    <= beatMs / SixteenthsPerBeat / 2
                )
            : Enumerable.Range(0, bins);

        var peak = candidates.MaxBy(bin =>
            Enumerable
                .Range(-SmoothingBins, 2 * SmoothingBins + 1)
                .Sum(step =>
                    weights[(bin + step + bins) % bins] * (SmoothingBins + 1 - Math.Abs(step))
                )
        );

        return (peak + 0.5) / 1000;
    }

    /// <summary>How far <paramref name="ms"/> is from the nearest multiple of <paramref name="period"/>.</summary>
    private static double Circular(double ms, double period)
    {
        var within = (ms % period + period) % period;

        return Math.Min(within, period - within);
    }

    /// <summary>The transients against the grid a warp lays down, so an offset of 0 is a transient on its line.</summary>
    public static GridReport Against(IReadOnlyList<double> times, Warp warp) =>
        warp is { Markers.Count: > 1 }
            ? Report(
                times,
                time => BeatAt(warp, time),
                seconds => 60 / (warp.TempoAt(seconds) ?? 120)
            )
            : new GridReport([], []);

    /// <summary>The transients against one steady tempo, phased to fit them all, so the offsets center on 0.</summary>
    public static GridReport Against(IReadOnlyList<double> times, double bpm)
    {
        var phase = Phase(times.Select(time => time * bpm / 60));

        return Report(times, time => time * bpm / 60 - phase / SixteenthsPerBeat, _ => 60 / bpm);
    }

    private static GridReport Report(
        IReadOnlyList<double> times,
        Func<double, double> beatAt,
        Func<double, double> secondsPerBeatAt
    )
    {
        if (times.Count is 0)
            return new GridReport([], []);

        List<GridWindow> windows = [];

        for (var start = 0.0; start < times[^1]; start += WindowSeconds)
        {
            var inside = times
                .Where(time => time >= start && time < start + WindowSeconds)
                .ToArray();

            if (inside.Length >= MinPerWindow)
                windows.Add(
                    new GridWindow(
                        start,
                        start + WindowSeconds,
                        Phase(inside.Select(beatAt))
                            * secondsPerBeatAt(start + WindowSeconds / 2)
                            / SixteenthsPerBeat
                            * 1000,
                        inside.Length
                    )
                );
        }

        return new GridReport(windows, Steps(windows));
    }

    /// <summary>Boundaries where the windows after sit a settled distance from the windows before, largest first within a stretch.</summary>
    private static List<GridStep> Steps(List<GridWindow> windows)
    {
        List<GridStep> steps = [];

        for (var i = StepWindows; i <= windows.Count - StepWindows; i++)
        {
            var parted =
                Median(windows.Skip(i).Take(StepWindows))
                - Median(windows.Skip(i - StepWindows).Take(StepWindows));

            if (Math.Abs(parted) < StepMs)
                continue;

            if (steps is [.., var last] && windows[i].Start - last.At < StepWindows * WindowSeconds)
            {
                if (Math.Abs(parted) > Math.Abs(last.Ms))
                    steps[^1] = new GridStep(windows[i].Start, parted);

                continue;
            }

            steps.Add(new GridStep(windows[i].Start, parted));
        }

        return steps;
    }

    private static double Median(IEnumerable<GridWindow> windows)
    {
        var sorted = windows.Select(window => window.OffsetMs).Order().ToArray();

        return sorted.Length % 2 is 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    /// <summary>The mean position within a 16th of the given beats, as a fraction of a 16th from -½ to ½.</summary>
    private static double Phase(IEnumerable<double> beats)
    {
        var (cos, sin, _) = Sum(beats);

        return Math.Atan2(sin, cos) / (2 * Math.PI);
    }

    private static double Concentration(IEnumerable<double> beats) =>
        Sum(beats) is (var cos, var sin, > 0 and var count)
            ? Math.Sqrt(cos * cos + sin * sin) / count
            : 0;

    /// <summary>Each beat as a point on the circle one 16th long, summed.</summary>
    private static (double Cos, double Sin, int Count) Sum(IEnumerable<double> beats)
    {
        var (cos, sin, count) = (0.0, 0.0, 0);

        foreach (var beat in beats)
        {
            var angle = 2 * Math.PI * beat * SixteenthsPerBeat;
            cos += Math.Cos(angle);
            sin += Math.Sin(angle);
            count++;
        }

        return (cos, sin, count);
    }

    /// <summary>The inverse of <see cref="Warp.SecondsAt"/>, extending the first and last segments.</summary>
    private static double BeatAt(Warp warp, double seconds)
    {
        var markers = warp.Markers;
        var i = 1;

        while (i < markers.Count - 1 && seconds > markers[i].Seconds)
            i++;

        var (from, to) = (markers[i - 1], markers[i]);

        return from.Beat
            + (seconds - from.Seconds) * (to.Beat - from.Beat) / (to.Seconds - from.Seconds);
    }
}

/// <param name="Windows">Each 10 s with enough transients, in order.</param>
/// <param name="Steps">Where the offset jumps and stays, as when a section was pasted a hair late.</param>
public sealed record GridReport(IReadOnlyList<GridWindow> Windows, IReadOnlyList<GridStep> Steps)
{
    /// <summary>How far the offsets move per minute, steps taken out; a wrong tempo shows here.</summary>
    public double DriftMsPerMinute
    {
        get
        {
            if (Windows.Count < 2)
                return 0;

            var points = Windows
                .Select(window =>
                    (
                        X: (window.Start + window.End) / 2 / 60,
                        Y: window.OffsetMs
                            - Steps.Where(step => step.At <= window.Start).Sum(step => step.Ms)
                    )
                )
                .ToArray();
            var (meanX, meanY) = (
                points.Average(point => point.X),
                points.Average(point => point.Y)
            );

            return points.Sum(point => (point.X - meanX) * (point.Y - meanY))
                / points.Sum(point => (point.X - meanX) * (point.X - meanX));
        }
    }

    /// <summary>One standard error of <see cref="DriftMsPerMinute"/>, from how far the windows scatter around their line.</summary>
    public double DriftNoiseMsPerMinute
    {
        get
        {
            if (Windows.Count < 3)
                return double.PositiveInfinity;

            var points = Windows
                .Select(window =>
                    (
                        X: (window.Start + window.End) / 2 / 60,
                        Y: window.OffsetMs
                            - Steps.Where(step => step.At <= window.Start).Sum(step => step.Ms)
                    )
                )
                .ToArray();
            var (meanX, meanY) = (
                points.Average(point => point.X),
                points.Average(point => point.Y)
            );
            var spreadX = points.Sum(point => (point.X - meanX) * (point.X - meanX));
            var slope = DriftMsPerMinute;
            var residuals = points.Sum(point =>
                Math.Pow(point.Y - meanY - slope * (point.X - meanX), 2)
            );

            return Math.Sqrt(residuals / (points.Length - 2) / spreadX);
        }
    }

    /// <summary>Where the windows sit on the whole, in ms; positive is late.</summary>
    public double MedianOffsetMs =>
        Windows.Select(window => window.OffsetMs).Order().ToArray() switch
        {
            [] => 0,
            var sorted when sorted.Length % 2 is 1 => sorted[sorted.Length / 2],
            var sorted => (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2,
        };

    /// <summary>The windows' mean offset in each stretch of this many seconds that holds any, in ms.</summary>
    public IEnumerable<double> OffsetsPer(double seconds) =>
        Windows
            .GroupBy(window => (int)(window.Start / seconds))
            .Select(group => group.Average(window => window.OffsetMs));

    /// <summary>The widest any window sits from another, in ms.</summary>
    public double SpreadMs =>
        Windows.Count is 0
            ? 0
            : Windows.Max(window => window.OffsetMs) - Windows.Min(window => window.OffsetMs);
}

/// <param name="OffsetMs">Where the window's transients sit on average against the grid; positive is late.</param>
public readonly record struct GridWindow(double Start, double End, double OffsetMs, int Transients);

/// <param name="At">The start of the first window after the step, in seconds.</param>
/// <param name="Ms">How far the offset moved; positive is later.</param>
public readonly record struct GridStep(double At, double Ms);
