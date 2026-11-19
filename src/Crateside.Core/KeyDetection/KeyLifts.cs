using System.Globalization;
using System.Text.RegularExpressions;

namespace Crateside.Core.KeyDetection;

/// <summary>A later passage that repeats an earlier one transposed up by <c>Shift</c> semitones, where the two-key fit agrees (ADR 0100).</summary>
/// <param name="At">Seconds into the track where the transposed copy starts.</param>
/// <param name="Strength">The matcher's mean correlation of the two passages, 0 to 1.</param>
/// <param name="Fit">The fit comparison's S at the agreeing peak; 0.081 is the floor.</param>
public sealed record KeyLift(double At, double Length, int Shift, double Strength, double Fit);

/// <summary>The lifts in a track, or why its chroma could not be read.</summary>
public abstract record KeyLiftReading
{
    private KeyLiftReading() { }

    public sealed record Read(IReadOnlyList<KeyLift> Lifts) : KeyLiftReading;

    public sealed record Failed(string Error) : KeyLiftReading;
}

/// <summary>One frame of keydetect's chroma: seconds into the track and the 12 pitch classes, C first.</summary>
public sealed record ChromaWindow(double Time, IReadOnlyList<double> Chroma);

/// <summary>
/// keydetect-harness report 15's agreement of the transposed-repeat matcher (report 12a) and the two-key fit comparison
/// (report 14), on keydetect's chroma; ported at the harness's default thresholds.
/// </summary>
public static partial class KeyLifts
{
    public const string Name = "keydetect_key_lifts";

    private const double SlotSeconds = 1;
    private const int SegmentSlots = 8;
    private const double SeedFloor = 0.65;
    private const double SeedMargin = 0.15;
    private const int MinimumRun = 2;
    private const double Keep = 0.5;

    private const double FitUnitSeconds = 0.5;
    private const int FitWidth = 64;
    private const int MinimumSide = 24;
    private const int PeakGap = 16;
    private const int PeakCount = 4;

    /// <summary>The 90th percentile of the largest S on report 15's 232 tracks with no labelled change.</summary>
    private const double FitFloor = 0.081;
    private const double AgreeSeconds = 10;

    private static readonly double[] KrumhanslMajor =
    [
        6.35,
        2.23,
        3.48,
        2.33,
        4.38,
        4.09,
        2.52,
        5.19,
        2.39,
        3.66,
        2.29,
        2.88,
    ];

    private static readonly double[] KrumhanslMinor =
    [
        6.33,
        2.68,
        3.52,
        5.38,
        2.60,
        3.53,
        2.54,
        4.75,
        3.98,
        2.69,
        3.34,
        3.17,
    ];

    /// <summary>Keys 0 to 11 major from C, 12 to 23 minor; each row centred and of unit length.</summary>
    private static readonly double[][] Profiles =
    [
        .. new[] { KrumhanslMajor, KrumhanslMinor }.SelectMany(profile =>
            Enumerable
                .Range(0, 12)
                .Select(tonic =>
                    Normalised([
                        .. Enumerable.Range(0, 12).Select(c => profile[(c - tonic + 12) % 12]),
                    ])
                )
        ),
    ];

    /// <summary>The lifts in the chroma, one per start and shift, the strongest copy kept.</summary>
    public static IReadOnlyList<KeyLift> Find(IReadOnlyList<ChromaWindow> windows)
    {
        if (windows is not [.., { Time: var last }])
            return [];

        var duration = last + 0.05;
        var peaks = FitPeaks(Means(windows, FitUnitSeconds, duration));

        return
        [
            .. Copies(Means(windows, SlotSeconds, duration), duration)
                .Select(copy =>
                    (
                        Copy: copy,
                        Fit: peaks
                            .Where(peak =>
                                Math.Abs(peak.Time - copy.At) <= AgreeSeconds
                                && peak.Shift == copy.Shift
                                && peak.S >= FitFloor
                            )
                            .Max(peak => (double?)peak.S)
                    )
                )
                .Where(agreed => agreed.Fit is not null)
                .GroupBy(agreed => (agreed.Copy.At, agreed.Copy.Shift))
                .Select(group => group.MaxBy(agreed => agreed.Copy.Strength))
                .Select(agreed => new KeyLift(
                    agreed.Copy.At,
                    Math.Round(agreed.Copy.Length, 2),
                    agreed.Copy.Shift,
                    Math.Round(agreed.Copy.Strength, 3),
                    Math.Round(agreed.Fit!.Value, 4)
                )),
        ];
    }

    /// <summary>The chroma frames ametadata prints from <c>keydetect=chroma=1</c>, in time order.</summary>
    internal static List<ChromaWindow> Parse(string stderr) =>
        [
            .. ChromaLine
                .Matches(stderr)
                .Select(match => new ChromaWindow(
                    double.Parse(match.Groups["Time"].Value, CultureInfo.InvariantCulture),
                    [
                        .. match
                            .Groups["Chroma"]
                            .Value.Split(',')
                            .Select(value => double.Parse(value, CultureInfo.InvariantCulture)),
                    ]
                ))
                .Where(window => window.Chroma.Count is 12)
                .OrderBy(window => window.Time),
        ];

    /// <summary>The mean chroma of each unit from 0 to the end, null where no frame falls.</summary>
    private static double[]?[] Means(IReadOnlyList<ChromaWindow> windows, double step, double end)
    {
        var count = (int)Math.Ceiling(end / step);
        var sums = new double[count][];
        var counts = new int[count];

        foreach (var window in windows)
        {
            var unit = (int)Math.Floor(window.Time / step);

            if (unit < 0 || unit >= count || window.Time >= end)
                continue;

            sums[unit] ??= new double[12];
            counts[unit]++;

            for (var c = 0; c < 12; c++)
                sums[unit][c] += window.Chroma[c];
        }

        return
        [
            .. sums.Select((sum, unit) => sum?.Select(value => value / counts[unit]).ToArray()),
        ];
    }

    // ---------------------------------------------------------------- the matcher (report 12a)

    private sealed record Copy(double At, double Length, int Shift, double Strength);

    private static List<Copy> Copies(double[]?[] means, double duration)
    {
        var n = means.Length;
        var units = means.Select(mean => mean is null ? null : Unit(mean)).ToArray();
        var m = n - SegmentSlots + 1;

        if (m <= SegmentSlots)
            return [];

        var whole = Enumerable
            .Range(0, m)
            .Select(a => Enumerable.Range(a, SegmentSlots).All(k => units[k] is not null))
            .ToArray();

        var best0 = new double[m];
        var bestOther = new double[m];
        var argOther = new (int A, int Shift)[m];
        Array.Fill(best0, double.NegativeInfinity);
        Array.Fill(bestOther, double.NegativeInfinity);

        // One diagonal (b - a) at a time, so memory stays linear; a segment pair's score sums its diagonal's dots.
        var dots = new double[n];

        for (var r = 0; r < 12; r++)
        {
            for (var lag = SegmentSlots; lag < m; lag++)
            {
                for (var a = 0; a + lag < n; a++)
                    dots[a] =
                        units[a] is { } left && units[a + lag] is { } right
                            ? Dot(left, right, r)
                            : 0;

                for (var a = 0; a + lag < m; a++)
                {
                    var b = a + lag;

                    if (!whole[a] || !whole[b])
                        continue;

                    var sum = 0.0;

                    for (var k = 0; k < SegmentSlots; k++)
                        sum += dots[a + k];

                    var s = sum / SegmentSlots;

                    if (r is 0)
                        best0[b] = Math.Max(best0[b], s);
                    // The harness takes the first best in shift-then-earlier-segment order.
                    else if (
                        s > bestOther[b]
                        || s == bestOther[b]
                            && (r, a).CompareTo((argOther[b].Shift, argOther[b].A)) < 0
                    )
                    {
                        bestOther[b] = s;
                        argOther[b] = (a, r);
                    }
                }
            }
        }

        var seed = Enumerable
            .Range(0, m)
            .Select(b =>
                double.IsFinite(bestOther[b])
                && double.IsFinite(best0[b])
                && bestOther[b] >= SeedFloor
                && bestOther[b] - best0[b] >= SeedMargin
            )
            .ToArray();

        List<Copy> copies = [];

        for (var first = 0; first < m; )
        {
            if (!seed[first])
            {
                first++;

                continue;
            }

            var shift = argOther[first].Shift;
            var last = first;

            while (last + 1 < m && seed[last + 1] && argOther[last + 1].Shift == shift)
                last++;

            if (last - first + 1 >= MinimumRun)
            {
                var j = first;

                for (var b = first + 1; b <= last; b++)
                    if (bestOther[b] > bestOther[j])
                        j = b;

                var (k0, k1) = Stretch(units, argOther[j].A, j, shift);
                var start = (j + k0) * SlotSeconds;
                var end = j + k1 + 1 < n ? (j + k1 + 1) * SlotSeconds : duration;
                copies.Add(new Copy(start, end - start, shift, bestOther[j]));
            }

            first = last + 1;
        }

        return copies;
    }

    /// <summary>The offsets around (a, b) where the 4-unit moving mean of the shifted correlation stays at <see cref="Keep"/> or above.</summary>
    private static (int K0, int K1) Stretch(double[]?[] units, int a, int b, int r)
    {
        var from = -Math.Min(a, b);
        var length = units.Length - Math.Max(a, b) - from;
        var correlation = Enumerable
            .Range(from, length)
            .Select(k =>
                units[a + k] is { } left && units[b + k] is { } right ? Dot(left, right, r) : 0
            )
            .ToArray();

        // numpy's convolve(c, ones(4) / 4, "same"): element i averages c[i - 2] to c[i + 1].
        var kept = Enumerable
            .Range(0, length)
            .Select(i =>
                Enumerable.Range(i - 2, 4).Where(k => k >= 0 && k < length).Sum(k => correlation[k])
                    / 4
                >= Keep
            )
            .ToArray();

        var low = -from;
        var high = low;

        while (low > 0 && kept[low - 1])
            low--;

        while (high < length - 1 && kept[high + 1])
            high++;

        return (low + from, high + from);
    }

    private static double Dot(double[] left, double[] right, int r)
    {
        var sum = 0.0;

        for (var c = 0; c < 12; c++)
            sum += left[c] * right[(c + r) % 12];

        return sum;
    }

    /// <summary>The vector less its mean, scaled to unit length, so a dot product is a correlation; null for a flat one.</summary>
    private static double[]? Unit(double[] vector) =>
        Centre(vector).Norm > 1e-6 ? Normalised(vector) : null;

    private static double[] Normalised(double[] vector)
    {
        var (centred, norm) = Centre(vector);

        return [.. centred.Select(value => value / norm)];
    }

    private static (double[] Centred, double Norm) Centre(double[] vector)
    {
        var mean = vector.Average();
        var centred = vector.Select(value => value - mean).ToArray();

        return (centred, Math.Sqrt(centred.Sum(value => value * value)));
    }

    // ---------------------------------------------------------------- the fit comparison (report 14)

    private sealed record FitPeak(double Time, int Shift, double S);

    private static List<FitPeak> FitPeaks(double[]?[] means)
    {
        var n = means.Length;

        if (n < 2 * MinimumSide + 1)
            return [];

        var sums = new double[n + 1][];
        sums[0] = new double[12];

        for (var i = 0; i < n; i++)
        {
            var share = Share(means[i]);
            sums[i + 1] = [.. sums[i].Select((value, c) => value + share[c])];
        }

        List<(int X, double S, int Shift)> scan = [];

        for (var x = MinimumSide; x <= n - MinimumSide; x++)
        {
            var (low, high) = (Math.Max(0, x - FitWidth), Math.Min(n, x + FitWidth));
            var (left, right, whole) = (
                Fits(sums, low, x),
                Fits(sums, x, high),
                Fits(sums, low, high)
            );
            var best = double.NegativeInfinity;
            var shift = 0;

            for (var from = 0; from < 24; from++)
            {
                for (var k = 1; k < 12; k++)
                {
                    var to = from < 12 ? (from + k) % 12 : 12 + (from - 12 + k) % 12;

                    if (left[from] + right[to] > best)
                        (best, shift) = (left[from] + right[to], k);
                }
            }

            scan.Add((x, (best - whole.Max()) / (high - low), shift));
        }

        List<(int X, double S, int Shift)> chosen = [];

        foreach (var point in scan.OrderByDescending(point => point.S))
        {
            if (chosen.All(kept => Math.Abs(kept.X - point.X) >= PeakGap))
                chosen.Add(point);

            if (chosen.Count is PeakCount)
                break;
        }

        return
        [
            .. chosen.Select(point => new FitPeak(point.X * FitUnitSeconds, point.Shift, point.S)),
        ];
    }

    /// <summary>The unit's chroma, negatives clipped, as shares of its sum; zero for a silent unit.</summary>
    private static double[] Share(double[]? mean)
    {
        if (mean is null)
            return new double[12];

        var clipped = mean.Select(value => Math.Max(0, value)).ToArray();
        var sum = clipped.Sum();

        return sum > 1e-9 ? [.. clipped.Select(value => value / sum)] : new double[12];
    }

    /// <summary>n·r of units [a, b) against each of the 24 keys.</summary>
    private static double[] Fits(double[][] sums, int a, int b)
    {
        var (centred, norm) = Centre([.. sums[b].Select((value, c) => value - sums[a][c])]);

        return norm < 1e-12
            ? new double[24]
            : [.. Profiles.Select(profile => (b - a) * Dot(profile, centred, 0) / norm)];
    }

    [GeneratedRegex(
        @"lavfi\.keydetect\.chroma\d+=(?<Time>[0-9.]+):(?<Chroma>[-0-9.,e]+)\s*$",
        RegexOptions.Multiline
    )]
    private static partial Regex ChromaLine { get; }
}
