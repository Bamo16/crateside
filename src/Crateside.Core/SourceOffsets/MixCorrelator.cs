namespace Crateside.Core.SourceOffsets;

/// <summary>
/// The mix transformed once, so two windows of an added source cost one more transform each way:
/// whitened (PHAT, 100 Hz to 10 kHz) cross-correlation of a window against every lag of the mix.
/// </summary>
internal sealed class MixCorrelator
{
    public const double LowHz = 100;
    public const double HighHz = 10000;

    private readonly float[] _spectrum;
    private readonly int _size;
    private readonly int _rate;
    private float[]? _work;

    public MixCorrelator(float[] mix, int rate, int longest)
    {
        MixLength = mix.Length;
        _rate = rate;
        _size = Fft.SizeFor(mix.Length + longest);
        _spectrum = new float[2 * _size];

        for (var i = 0; i < mix.Length; i++)
            _spectrum[2 * i] = mix[i];

        Fft.Transform(_spectrum);
    }

    public int MixLength { get; }

    /// <summary>Correlation of two windows at every lag from <c>-(length - 1)</c> to <c>MixLength - 1</c>, as <see cref="Correlation"/>s.</summary>
    public (Correlation First, Correlation? Second) Correlate(
        ReadOnlySpan<float> first,
        ReadOnlySpan<float> second
    )
    {
        var work = _work ??= new float[2 * _size];

        Array.Clear(work);

        for (var i = 0; i < first.Length; i++)
            work[2 * i] = first[i];

        for (var i = 0; i < second.Length; i++)
            work[(2 * i) + 1] = second[i];

        Fft.Transform(work);

        var (maxFirst, maxSecond) = Maxima(work);

        Whiten(work, maxFirst, maxSecond);
        Fft.Transform(work, inverse: true);

        return (
            Extract(work, 0, first.Length),
            second.IsEmpty ? null : Extract(work, 1, second.Length)
        );
    }

    private (double First, double Second) Maxima(float[] work)
    {
        var half = _size / 2;
        var partial = new (double First, double Second)[Environment.ProcessorCount * 4];
        var span = (half + partial.Length - 1) / partial.Length;

        Cpu.For(
            0,
            partial.Length,
            part =>
            {
                double maxFirst = 0,
                    maxSecond = 0;

                for (var k = part * span; k < Math.Min(half + 1, (part + 1) * span); k++)
                {
                    var (first, second) = Split(work, k);
                    var mix = Magnitude(_spectrum[2 * k], _spectrum[(2 * k) + 1]);

                    maxFirst = Math.Max(maxFirst, mix * Magnitude(first.Re, first.Im));
                    maxSecond = Math.Max(maxSecond, mix * Magnitude(second.Re, second.Im));
                }

                partial[part] = (maxFirst, maxSecond);
            }
        );

        return (partial.Max(part => part.First), partial.Max(part => part.Second));
    }

    private void Whiten(float[] work, double maxFirst, double maxSecond)
    {
        var half = _size / 2;
        var lowBin = (int)Math.Ceiling(LowHz * _size / _rate);
        var highBin = (int)Math.Floor(HighHz * _size / _rate);
        var floorFirst = (1e-9 * maxFirst) + 1e-20;
        var floorSecond = (1e-9 * maxSecond) + 1e-20;
        var chunks = Environment.ProcessorCount * 4;
        var span = (half + chunks - 1) / chunks;

        Cpu.For(
            0,
            chunks,
            part =>
            {
                for (var k = part * span; k < Math.Min(half + 1, (part + 1) * span); k++)
                {
                    var inBand = k >= lowBin && k <= highBin && k > 0 && k < half;

                    if (!inBand)
                    {
                        work[2 * k] = work[(2 * k) + 1] = 0;

                        if (k > 0 && k < half)
                            work[2 * (_size - k)] = work[(2 * (_size - k)) + 1] = 0;

                        continue;
                    }

                    var (first, second) = Split(work, k);
                    var (cFirstRe, cFirstIm) = Normalized(k, first, floorFirst);
                    var (cSecondRe, cSecondIm) = Normalized(k, second, floorSecond);

                    // Y = C1 + i C2 at k; conj(C1) + i conj(C2) at the mirrored bin.
                    work[2 * k] = (float)(cFirstRe - cSecondIm);
                    work[(2 * k) + 1] = (float)(cFirstIm + cSecondRe);

                    var mirror = _size - k;

                    work[2 * mirror] = (float)(cFirstRe + cSecondIm);
                    work[(2 * mirror) + 1] = (float)(-cFirstIm + cSecondRe);
                }
            }
        );
    }

    private (double Re, double Im) Normalized(int k, (double Re, double Im) window, double floor)
    {
        // mix * conj(window)
        var (mixRe, mixIm) = (_spectrum[2 * k], _spectrum[(2 * k) + 1]);
        var re = (mixRe * window.Re) + (mixIm * window.Im);
        var im = (mixIm * window.Re) - (mixRe * window.Im);
        var scale = Magnitude(re, im) + floor;

        return (re / scale, im / scale);
    }

    private (double Re, double Im) Of(float[] work, int k) => (work[2 * k], work[(2 * k) + 1]);

    /// <summary>The two real signals' spectra at bin k, from the one transform of first + i second.</summary>
    private ((double Re, double Im) First, (double Re, double Im) Second) Split(float[] work, int k)
    {
        var (zRe, zIm) = Of(work, k);
        var (nRe, nIm) = Of(work, k == 0 ? 0 : _size - k);

        // First = (Z + conj(Zn)) / 2; Second = (Z - conj(Zn)) / 2i.
        return (((zRe + nRe) / 2, (zIm - nIm) / 2), ((zIm + nIm) / 2, -(zRe - nRe) / 2));
    }

    private static double Magnitude(double re, double im) => Math.Sqrt((re * re) + (im * im));

    private Correlation Extract(float[] work, int part, int length)
    {
        var values = new float[MixLength + length - 1];

        for (var i = 0; i < values.Length; i++)
        {
            var lag = i - (length - 1);
            var index = lag >= 0 ? lag : lag + _size;

            values[i] = work[(2 * index) + part];
        }

        return new Correlation(values, length - 1);
    }
}

/// <param name="Values">The correlation at each lag from <c>-LagBias</c> upward.</param>
internal sealed record Correlation(float[] Values, int LagBias)
{
    public int LagOf(int index) => index - LagBias;

    public int IndexOfLag(int lag) => lag + LagBias;

    public int ArgMax() => ArgMax(0, Values.Length - 1);

    /// <summary>The index of the highest value between two indexes, both included.</summary>
    public int ArgMax(int from, int to)
    {
        var best = from;

        for (var i = from + 1; i <= to; i++)
        {
            if (Values[i] > Values[best])
                best = i;
        }

        return best;
    }

    /// <summary>Peak height over the correlation's own noise: median and MAD-scaled width of every lag.</summary>
    public double Psr(int index)
    {
        var noise = Median([.. Values]);
        var deviations = new float[Values.Length];

        Cpu.For(
            0,
            Environment.ProcessorCount * 4,
            part =>
            {
                var span =
                    (Values.Length + (Environment.ProcessorCount * 4) - 1)
                    / (Environment.ProcessorCount * 4);

                for (var i = part * span; i < Math.Min(Values.Length, (part + 1) * span); i++)
                    deviations[i] = (float)Math.Abs(Values[i] - noise);
            }
        );

        var width = (1.4826 * Median(deviations)) + 1e-30;

        return (Values[index] - noise) / width;
    }

    private static double Median(float[] values)
    {
        var middle = values.Length / 2;
        var upper = Select(values, middle);

        if (values.Length % 2 is 1)
            return upper;

        var lower = float.MinValue;

        foreach (var value in values.AsSpan(0, middle))
            lower = Math.Max(lower, value);

        return (lower + (double)upper) / 2;
    }

    /// <summary>The k-th smallest value, found by partitioning in place.</summary>
    private static float Select(float[] values, int k)
    {
        int low = 0,
            high = values.Length - 1;

        while (low < high)
        {
            var pivot = values[low + ((high - low) / 2)];
            int i = low,
                j = high;

            while (i <= j)
            {
                while (values[i] < pivot)
                    i++;

                while (values[j] > pivot)
                    j--;

                if (i > j)
                    break;

                (values[i], values[j]) = (values[j], values[i]);
                i++;
                j--;
            }

            if (k <= j)
                high = j;
            else if (k >= i)
                low = i;
            else
                return values[k];
        }

        return values[k];
    }
}
