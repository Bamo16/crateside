using System.Numerics;
using AbleKit.Analysis;

namespace Crateside.Core.Warps;

/// <summary>
/// Finds transients the way Live marks them: onsets by spectral flux, each placed at the start of
/// its attack, scored against Live's own (docs/notes/2026-10-02-transients-against-lives.md).
/// </summary>
public sealed class OnsetDetector(OnsetSettings? settings = null) : ITransientDetector
{
    private const float Silence = 1e-6f;

    private readonly OnsetSettings _settings = settings ?? new OnsetSettings();

    /// <summary>Raise it whenever a change moves what the detector finds.</summary>
    public string Version => "onsets 1";

    public IReadOnlyList<Transient> Detect(DecodedAudio audio)
    {
        var s = _settings;
        var mono = Mono(audio);
        var bins =
            s.MaxFrequency > 0
                ? (int)(s.MaxFrequency * s.WindowSize / audio.SampleRate)
                : s.WindowSize / 2;
        var flux = Flux(mono, s.WindowSize, s.HopSize, s.Compression, s.MaxFilterBins, bins + 1);
        var hopsPerSecond = (double)audio.SampleRate / s.HopSize;
        var peaks = PickPeaks(
            flux,
            (int)Math.Round(s.PeakWindowSeconds * hopsPerSecond),
            (int)Math.Round(s.AverageWindowSeconds * hopsPerSecond),
            s.Delta,
            (int)Math.Round(s.MinimumGapSeconds * hopsPerSecond)
        );

        if (peaks is [])
            return [];

        var envelope = Envelope(mono, s.EnvelopeHop);
        var strongest = peaks.Max(peak => flux[peak]);
        List<Transient> transients = [];

        foreach (var peak in peaks)
        {
            // The flux frame is centered on its window; the attack it saw starts before that.
            var center = peak * s.HopSize + s.WindowSize / 2;
            var start = AttackStart(envelope, s.EnvelopeHop, center, audio.SampleRate, s);
            var position = Math.Clamp(start, 0, audio.Frames - 1);

            if (transients is [.., { Position: var lastPosition }] && position < lastPosition)
                position = lastPosition;

            transients.Add(new Transient(position, flux[peak] / strongest));
        }

        return transients;
    }

    private static float[] Mono(DecodedAudio audio)
    {
        var channels = audio.ChannelCount;
        var mono = new float[audio.Frames];

        for (var frame = 0; frame < mono.Length; frame++)
        {
            var sum = 0f;

            for (var channel = 0; channel < channels; channel++)
                sum += audio.Samples[frame * channels + channel];

            mono[frame] = sum / channels;
        }

        return mono;
    }

    /// <summary>Positive change in log-compressed magnitude per hop, against a widened previous frame.</summary>
    private static float[] Flux(
        float[] mono,
        int windowSize,
        int hopSize,
        float compression,
        int maxFilterBins,
        int bins
    )
    {
        var frames = Math.Max(0, (mono.Length - windowSize) / hopSize + 1);
        var window = Hann(windowSize);
        var buffer = new Complex[windowSize];
        var previous = new float[bins];
        var current = new float[bins];
        var widened = new float[bins];
        var flux = new float[frames];

        for (var t = 0; t < frames; t++)
        {
            var offset = t * hopSize;

            for (var i = 0; i < windowSize; i++)
                buffer[i] = new Complex(mono[offset + i] * window[i], 0);

            Fft.Transform(buffer);

            for (var k = 0; k < bins; k++)
                current[k] = MathF.Log(1 + compression * (float)buffer[k].Magnitude);

            for (var k = 0; k < bins; k++)
            {
                var max = previous[k];

                for (
                    var j = Math.Max(0, k - maxFilterBins);
                    j <= Math.Min(bins - 1, k + maxFilterBins);
                    j++
                )
                    max = Math.Max(max, previous[j]);

                widened[k] = max;
            }

            var sum = 0f;

            for (var k = 0; k < bins; k++)
                sum += Math.Max(0, current[k] - widened[k]);

            flux[t] = t is 0 ? 0 : sum;
            (previous, current) = (current, previous);
        }

        return flux;
    }

    /// <summary>Local maxima above the surrounding average by <paramref name="delta"/>, at least a gap apart.</summary>
    private static List<int> PickPeaks(
        float[] flux,
        int peakWindow,
        int averageWindow,
        float delta,
        int minimumGap
    )
    {
        List<int> peaks = [];

        if (flux.Length is 0)
            return peaks;

        var scale = flux.Max();

        if (scale <= 0)
            return peaks;

        var prefix = new double[flux.Length + 1];

        for (var t = 0; t < flux.Length; t++)
            prefix[t + 1] = prefix[t] + flux[t] / scale;

        for (var t = 1; t < flux.Length - 1; t++)
        {
            var value = flux[t] / scale;
            var from = Math.Max(0, t - peakWindow);
            var to = Math.Min(flux.Length - 1, t + peakWindow);
            var isMax = true;

            for (var j = from; j <= to && isMax; j++)
                isMax = flux[j] <= flux[t];

            if (!isMax)
                continue;

            var lo = Math.Max(0, t - averageWindow);
            var hi = Math.Min(flux.Length, t + averageWindow + 1);
            var average = (prefix[hi] - prefix[lo]) / (hi - lo);

            if (value < average + delta)
                continue;

            if (peaks is [.., var last] && t - last < minimumGap)
            {
                if (flux[t] > flux[last])
                    peaks[^1] = t;

                continue;
            }

            peaks.Add(t);
        }

        return peaks;
    }

    /// <summary>Mean absolute amplitude per <paramref name="hop"/> frames.</summary>
    private static float[] Envelope(float[] mono, int hop)
    {
        var envelope = new float[(mono.Length + hop - 1) / hop];

        for (var i = 0; i < envelope.Length; i++)
        {
            var sum = 0f;
            var end = Math.Min(mono.Length, (i + 1) * hop);

            for (var j = i * hop; j < end; j++)
                sum += Math.Abs(mono[j]);

            envelope[i] = sum / (end - i * hop);
        }

        return envelope;
    }

    /// <summary>Where the attack around <paramref name="center"/> crosses halfway from its floor to its peak, in decibels.</summary>
    private static int AttackStart(
        float[] envelope,
        int hop,
        int center,
        int sampleRate,
        OnsetSettings s
    )
    {
        var search = (int)(s.BacktrackSeconds * sampleRate / hop);
        var around = Math.Clamp(center / hop, 0, envelope.Length - 1);
        var peakFrom = around;
        var peakTo = Math.Min(
            envelope.Length - 1,
            around + (int)(s.PeakAheadSeconds * sampleRate / hop)
        );
        var peak = peakFrom;

        for (var i = peakFrom; i <= peakTo; i++)
            if (envelope[i] > envelope[peak])
                peak = i;

        var floorFrom = Math.Max(0, peak - search);
        var floor = peak;

        for (var i = peak; i >= floorFrom; i--)
            if (envelope[i] < envelope[floor])
                floor = i;

        // Halfway up the rise in decibels, which is where Live puts a transient on a soft attack
        // as on a sharp one.
        var floorLevel = MathF.Log10(envelope[floor] + Silence);
        var level =
            floorLevel + s.AttackFraction * (MathF.Log10(envelope[peak] + Silence) - floorLevel);
        var start = peak;

        while (start > floor && MathF.Log10(envelope[start - 1] + Silence) > level)
            start--;

        return start * hop;
    }

    private static float[] Hann(int size)
    {
        var window = new float[size];

        for (var i = 0; i < size; i++)
            window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / size);

        return window;
    }
}

/// <summary>The detector's tunings; the defaults are the ones scored in the note.</summary>
public sealed record OnsetSettings
{
    public int WindowSize { get; init; } = 1024;
    public int HopSize { get; init; } = 256;
    public float Compression { get; init; } = 100;
    public int MaxFilterBins { get; init; } = 1;

    /// <summary>The highest frequency the flux reads, in Hz; 0 reads them all.</summary>
    public double MaxFrequency { get; init; } = 8000;
    public double PeakWindowSeconds { get; init; } = 0.03;
    public double AverageWindowSeconds { get; init; } = 0.1;

    /// <summary>How far above its surroundings a flux peak must stand, as a fraction of the loudest.</summary>
    public float Delta { get; init; } = 0.02f;
    public double MinimumGapSeconds { get; init; } = 0.07;
    public int EnvelopeHop { get; init; } = 32;
    public double BacktrackSeconds { get; init; } = 0.04;
    public double PeakAheadSeconds { get; init; } = 0.02;

    /// <summary>Where on the attack's rise, in decibels from its floor (0) to its peak (1), the transient goes.</summary>
    public float AttackFraction { get; init; } = 0.5f;
}

/// <summary>An in-place radix-2 FFT; the length must be a power of two.</summary>
internal static class Fft
{
    public static void Transform(Complex[] data)
    {
        var n = data.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;

            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;

            j ^= bit;

            if (i < j)
                (data[i], data[j]) = (data[j], data[i]);
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;

                for (var j = 0; j < length / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + length / 2] * w;
                    data[i + j] = u + v;
                    data[i + j + length / 2] = u - v;
                    w *= step;
                }
            }
        }
    }
}
