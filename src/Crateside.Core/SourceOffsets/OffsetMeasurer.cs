namespace Crateside.Core.SourceOffsets;

/// <summary>
/// Lines an added source up to its mix by their shared audio and reads the verdict (ADR 0111): 5 s
/// windows, each correlated against the whole mix, only the confident ones voting.
/// </summary>
public static class OffsetMeasurer
{
    public const double WindowSeconds = 5;

    /// <summary>Known speed changes tried, as up/down, before a source is read as not the same recording.</summary>
    private static readonly (int Up, int Down)[] SpeedChanges =
    [
        (1000, 1001),
        (1001, 1000),
        (24, 25),
        (25, 24),
    ];

    private const double NearSeconds = 1;

    public static OffsetReading Measure(
        float[] mix,
        float[] source,
        int sampleRate,
        CancellationToken ct = default
    )
    {
        var window = (int)(WindowSeconds * sampleRate);
        var correlator = new MixCorrelator(mix, sampleRate, window);
        var coarse = new Coarse(mix, sampleRate, (int)(source.Length * 1.05) + CoarseStep);
        var reading = Read(correlator, coarse, source, sampleRate, ct);

        if (reading.Verdict is not (OffsetVerdict.NotTheSameRecording or OffsetVerdict.Edit))
            return reading;

        foreach (var (up, down) in SpeedChanges)
        {
            var stretched = Read(
                correlator,
                coarse,
                Resampler.Resample(source, up, down),
                sampleRate,
                ct
            );

            if (stretched.Verdict is OffsetVerdict.Aligned)
                return AtSpeed(stretched, up, down);
        }

        return reading;
    }

    private static OffsetReading AtSpeed(OffsetReading reading, int up, int down) =>
        reading with
        {
            Verdict = OffsetVerdict.Drift,
            Offset = null,
            DriftPpm = ((double)up / down - 1) * 1e6,
            SpeedFactor = $"{up}/{down}",
            Windows =
            [
                .. reading.Windows.Select(window =>
                    window with
                    {
                        Start = window.Start * down / up,
                    }
                ),
            ],
            Runs = [],
            OnlyInSource = [],
            OnlyInMix = [],
            Repeated = [],
        };

    private static OffsetReading Read(
        MixCorrelator correlator,
        Coarse coarse,
        float[] source,
        int rate,
        CancellationToken ct
    )
    {
        var size = (int)(WindowSeconds * rate);
        var starts = Starts(source.Length, size);
        var rows = starts.Select(start => new OffsetRow((double)start / rate)).ToArray();
        var awake = new List<int>();

        for (var i = 0; i < rows.Length; i++)
        {
            if (Rms(Segment(source, starts[i], size)) < OffsetVerdicts.SilentRms)
                rows[i].Kind = RowKind.Silent;
            else
                awake.Add(i);
        }

        var far = new (int Lag, double Psr)[rows.Length];

        foreach (var pair in awake.Chunk(2))
        {
            ct.ThrowIfCancellationRequested();

            var (first, second) = correlator.Correlate(
                Segment(source, starts[pair[0]], size),
                pair.Length > 1 ? Segment(source, starts[pair[1]], size) : []
            );

            Parallel.Invoke(
                () => far[pair[0]] = PeakOf(first),
                () =>
                {
                    if (second is not null)
                        far[pair[1]] = PeakOf(second);
                }
            );
        }

        var centreOffset = Centre(awake is [] ? 0 : coarse.OffsetOf(source), awake, far, starts);
        var reach = (int)(NearSeconds * rate);
        var farAway = new List<int>();

        foreach (var i in awake)
        {
            rows[i].Far = (far[i].Lag - starts[i], far[i].Psr);

            if (Math.Abs(far[i].Lag - (starts[i] + centreOffset)) <= reach)
                rows[i].Near = rows[i].Far;
            else
                farAway.Add(i);
        }

        foreach (var pair in farAway.Chunk(2))
        {
            ct.ThrowIfCancellationRequested();

            var (first, second) = correlator.Correlate(
                Segment(source, starts[pair[0]], size),
                pair.Length > 1 ? Segment(source, starts[pair[1]], size) : []
            );

            Parallel.Invoke(
                () =>
                    rows[pair[0]].Near = NearOf(
                        first,
                        starts[pair[0]],
                        centreOffset,
                        reach,
                        correlator.MixLength
                    ),
                () =>
                {
                    if (second is not null)
                        rows[pair[1]].Near = NearOf(
                            second,
                            starts[pair[1]],
                            centreOffset,
                            reach,
                            correlator.MixLength
                        );
                }
            );
        }

        foreach (
            var row in rows.Where(row => !row.IsSilent && row.Near.Psr >= OffsetVerdicts.MinPsr)
        )
            row.Kind = RowKind.Near;

        OffsetVerdicts.MarkEdits(rows, rate);

        return OffsetVerdicts.Decide(
            rows,
            rate,
            WindowSeconds,
            (double)source.Length / rate,
            (double)correlator.MixLength / rate
        );
    }

    private static List<int> Starts(int length, int size)
    {
        List<int> starts =
        [
            .. Enumerable.Range(0, Math.Max(1, ((length - size) / size) + 1)).Select(i => i * size),
        ];

        if (length > size && starts[^1] + size < length)
            starts.Add(length - size);

        return starts;
    }

    private static float[] Segment(float[] source, int start, int size) =>
        source[start..Math.Min(source.Length, start + size)];

    private static double Rms(float[] segment) =>
        Math.Sqrt(segment.Sum(value => (double)value * value) / segment.Length);

    private static (int Lag, double Psr) PeakOf(Correlation correlation)
    {
        var index = correlation.ArgMax();

        return (correlation.LagOf(index), correlation.Psr(index));
    }

    private static (int Offset, double Psr) NearOf(
        Correlation correlation,
        int start,
        int centreOffset,
        int reach,
        int mixLength
    )
    {
        var from = Math.Max(correlation.LagOf(0), start + centreOffset - reach);
        var to = Math.Min(mixLength - 1, start + centreOffset + reach);
        var index =
            from > to
                ? correlation.ArgMax()
                : correlation.ArgMax(correlation.IndexOfLag(from), correlation.IndexOfLag(to));

        return (correlation.LagOf(index) - start, correlation.Psr(index));
    }

    /// <summary>
    /// The whole-file peak, as the offset most of the file sits on, settled to the sample by the
    /// confident windows that agree with it.
    /// </summary>
    private static int Centre(
        int global,
        List<int> awake,
        (int Lag, double Psr)[] far,
        List<int> starts
    ) =>
        awake
            .Where(i => far[i].Psr >= OffsetVerdicts.MinPsr)
            .Select(i => far[i].Lag - starts[i])
            .Where(offset => Math.Abs(offset - global) <= CoarseStep * 4)
            .Order()
            .ToList()
            is { Count: > 0 } near
            ? near[near.Count / 2]
            : global;

    private const int CoarseStep = 4;

    /// <summary>The mix at a quarter of the rate, to find where a whole source sits in it cheaply.</summary>
    private sealed class Coarse
    {
        private readonly MixCorrelator _mix;
        private readonly int _longest;

        public Coarse(float[] mix, int rate, int longest)
        {
            _longest = longest / CoarseStep;
            _mix = new MixCorrelator(
                Resampler.Resample(mix, 1, CoarseStep),
                rate / CoarseStep,
                _longest
            );
        }

        public int OffsetOf(float[] source)
        {
            var coarse = Resampler.Resample(source, 1, CoarseStep);
            var (first, _) = _mix.Correlate(coarse[..Math.Min(coarse.Length, _longest)], []);

            return first.LagOf(first.ArgMax()) * CoarseStep;
        }
    }
}
