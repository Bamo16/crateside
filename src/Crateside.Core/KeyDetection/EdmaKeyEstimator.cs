using Crateside.Core.Pairing;

namespace Crateside.Core.KeyDetection;

/// <summary>
/// Key by template matching (Gómez's HPCP against Faraldo et al.'s <c>edma</c> profiles), set up to
/// reproduce Essentia's <c>KeyExtractor(profileType="edma")</c>; written from the published
/// methods and checked against Essentia's output, not from its source (ADR 0055).
/// </summary>
public static class EdmaKeyEstimator
{
    public const string Name = "essentia_edma";

    private const int SampleRate = 44100;
    private const int FrameSize = 4096;
    private const int Bins = FrameSize / 2 + 1;
    private const double BinHz = SampleRate / 2.0 / (Bins - 1);

    /// <summary>Electronic dance music profiles (Faraldo et al. 2016), from A as Essentia orders them.</summary>
    private static readonly double[] MajorProfile =
    [
        1.00,
        0.29,
        0.50,
        0.40,
        0.60,
        0.56,
        0.32,
        0.80,
        0.31,
        0.45,
        0.42,
        0.39,
    ];

    private static readonly double[] MinorProfile =
    [
        1.00,
        0.31,
        0.44,
        0.58,
        0.33,
        0.49,
        0.29,
        0.78,
        0.43,
        0.29,
        0.53,
        0.32,
    ];

    private static readonly double[] Window = NormalizedHann(FrameSize);

    /// <summary>Over 44.1 kHz mono samples.</summary>
    public static KeyReading Read(ReadOnlySpan<float> samples)
    {
        var pcp = new double[12];
        var fft = new Fft(FrameSize);
        var frame = new double[FrameSize];
        var spectrum = new double[Bins];
        var frames = (samples.Length + FrameSize / 2 + FrameSize - 1) / FrameSize;

        for (var f = 0; f < frames; f++)
        {
            // Frames start half a frame before the audio, as Essentia's FrameCutter does by default.
            var start = f * FrameSize - FrameSize / 2;
            for (var i = 0; i < FrameSize; i++)
            {
                var index = start + i;
                frame[i] = index >= 0 && index < samples.Length ? samples[index] * Window[i] : 0;
            }

            fft.Magnitudes(frame, spectrum, Bins);

            var peaks = SpectralWhitening.Apply(spectrum, SpectralPeaks.Find(spectrum));
            Hpcp.Accumulate(peaks, pcp);
        }

        var max = pcp.Max();
        if (frames is 0 || max <= 0)
            return new KeyReading.Failed("no tonal content for Essentia edma");

        // The average over frames, scaled to its peak, with weak pitch classes dropped.
        for (var i = 0; i < 12; i++)
            pcp[i] = pcp[i] / max < 0.2 ? 0 : pcp[i] / max;

        var best = (Correlation: double.NegativeInfinity, Tonic: 0, Mode: Mode.Major);
        foreach (
            var (profile, mode) in new[] { (MajorProfile, Mode.Major), (MinorProfile, Mode.Minor) }
        )
        {
            for (var tonic = 0; tonic < 12; tonic++)
            {
                var r = Pearson(pcp, profile, tonic);
                if (r > best.Correlation)
                    best = (r, tonic, mode);
            }
        }

        // Pitch class 0 is A.
        return new KeyReading.Read(
            KeyReading.KeyOf((best.Tonic + 9) % 12, best.Mode),
            best.Correlation
        );
    }

    /// <summary>Pearson's r between the profile and the PCP rotated so the tonic sits at the profile's start.</summary>
    private static double Pearson(double[] pcp, double[] profile, int tonic)
    {
        var meanPcp = pcp.Average();
        var meanProfile = profile.Average();
        double covariance = 0,
            varPcp = 0,
            varProfile = 0;

        for (var i = 0; i < 12; i++)
        {
            var x = pcp[(i + tonic) % 12] - meanPcp;
            var y = profile[i] - meanProfile;
            covariance += x * y;
            varPcp += x * x;
            varProfile += y * y;
        }

        return varPcp is 0 ? double.NegativeInfinity : covariance / Math.Sqrt(varPcp * varProfile);
    }

    /// <summary>A symmetric Hann window scaled to sum to 2.</summary>
    private static double[] NormalizedHann(int size)
    {
        var window = Enumerable
            .Range(0, size)
            .Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (size - 1)))
            .ToArray();
        var scale = 2 / window.Sum();

        return [.. window.Select(value => value * scale)];
    }

    /// <summary>A spectral peak: its interpolated frequency and magnitude.</summary>
    internal sealed record Peak(double Frequency, double Magnitude);

    /// <summary>The 60 strongest local maxima between 25 and 3500 Hz, by parabolic interpolation.</summary>
    internal static class SpectralPeaks
    {
        private const int MaxPeaks = 60;
        private const double Threshold = 1e-4;
        private static readonly int First = (int)Math.Ceiling(25 / BinHz);
        private static readonly int Last = (int)Math.Ceiling(3500 / BinHz);

        public static List<Peak> Find(double[] spectrum)
        {
            List<Peak> peaks = [];

            // An edge of the range is a peak when it beats its one neighbor inside it.
            if (spectrum[First] > spectrum[First + 1] && spectrum[First] > Threshold)
                peaks.Add(new Peak(First * BinHz, spectrum[First]));

            for (var k = First + 1; k < Last; k++)
            {
                var (left, center, right) = (spectrum[k - 1], spectrum[k], spectrum[k + 1]);
                if (center > left && center >= right && center > Threshold)
                {
                    var offset = 0.5 * (left - right) / (left - 2 * center + right);
                    peaks.Add(
                        new Peak((k + offset) * BinHz, center - 0.25 * (left - right) * offset)
                    );
                }
            }

            if (spectrum[Last] > spectrum[Last - 1] && spectrum[Last] > Threshold)
                peaks.Add(new Peak(Last * BinHz, spectrum[Last]));

            return
            [
                .. peaks
                    .Where(peak => peak.Frequency <= 3500)
                    .OrderByDescending(peak => peak.Magnitude)
                    .Take(MaxPeaks),
            ];
        }
    }

    /// <summary>
    /// Scales each peak against a spectral envelope so strong and weak partials count alike, as
    /// Essentia's SpectralWhitening does (measured, docs/notes/2026-10-16-porting-the-key-detectors.md).
    /// </summary>
    internal static class SpectralWhitening
    {
        private const double BreakpointHz = 100;
        private static readonly (int First, double[] Weights)[] Kernels = BuildKernels();

        public static List<Peak> Apply(double[] spectrum, List<Peak> peaks)
        {
            if (peaks.Count is 0)
                return peaks;

            var envelope = Envelope(spectrum);

            return [.. peaks.Select(peak => Whitened(peak, envelope))];
        }

        private static Peak Whitened(Peak peak, double[] envelope)
        {
            var position = peak.Frequency / BreakpointHz;
            var index = Math.Min((int)position, envelope.Length - 2);
            var fraction = position - index;
            var envelopeDb = envelope[index] + fraction * (envelope[index + 1] - envelope[index]);

            // At most the envelope; 30 dB or more beneath it counts as nothing.
            var relativeDb = Math.Min(20 * Math.Log10(peak.Magnitude) - envelopeDb, 0);
            if (relativeDb <= -30)
                relativeDb = -200;

            return peak with
            {
                Magnitude = Math.Pow(10, (relativeDb - peak.Frequency / 200) / 20),
            };
        }

        /// <summary>At each breakpoint, the power-weighted mean power under a (1 − |x|)⁴ kernel, in dB.</summary>
        private static double[] Envelope(double[] spectrum)
        {
            var envelope = new double[Kernels.Length];

            for (var b = 0; b < Kernels.Length; b++)
            {
                var (first, weights) = Kernels[b];
                double numerator = 0,
                    denominator = 0;
                for (var i = 0; i < weights.Length; i++)
                {
                    var power = spectrum[first + i] * spectrum[first + i];
                    numerator += weights[i] * power * power;
                    denominator += weights[i] * power;
                }

                envelope[b] = 10 * Math.Log10(numerator / denominator);
            }

            return envelope;
        }

        /// <summary>Breakpoints every 100 Hz to 3600; each kernel spans 0.66 to 1.58 times its frequency.</summary>
        private static (int, double[])[] BuildKernels() =>
            [
                .. Enumerable
                    .Range(0, 37)
                    .Select(b =>
                    {
                        var frequency = b * BreakpointHz;
                        var (low, high) = b switch
                        {
                            // The two lowest are narrower than the rule gives (measured).
                            0 => (0, 5),
                            1 => (5, 15),
                            _ => (
                                (int)Math.Round(0.66 * frequency / BinHz),
                                (int)Math.Round(1.58 * frequency / BinHz)
                            ),
                        };
                        var center = (low + high) / 2.0;
                        var halfWidth = (high - low) / 2.0;

                        return (
                            low,
                            Enumerable
                                .Range(low, high - low + 1)
                                .Select(bin => Math.Pow(1 - Math.Abs(bin - center) / halfWidth, 4))
                                .ToArray()
                        );
                    }),
            ];
    }

    /// <summary>
    /// Harmonic pitch class profile (Gómez 2006): each peak's power credited to its pitch class and
    /// those of its first four subharmonics, through a one-semitone cosine window.
    /// </summary>
    internal static class Hpcp
    {
        private const int Subharmonics = 4;

        public static void Accumulate(IEnumerable<Peak> peaks, double[] pcp)
        {
            Span<double> amplitude = stackalloc double[12];
            Span<double> window = stackalloc double[12];

            foreach (var peak in peaks)
            {
                if (peak.Frequency is < 25 or > 3500)
                    continue;

                // Octave-related hypotheses share a pitch class and its window, and add as amplitudes.
                amplitude.Clear();
                var semitones = 12 * Math.Log2(peak.Frequency / 440);

                for (var h = 1; h <= Subharmonics + 1; h++)
                {
                    var pitch = semitones - 12 * Math.Log2(h);
                    var nearest = Math.Round(pitch);
                    var pitchClass = (int)((nearest % 12 + 12) % 12);
                    amplitude[pitchClass] += h is 1 ? 1 : Math.Min(1, 2 / Math.Log2(h));
                    window[pitchClass] = Math.Cos(Math.PI * (pitch - nearest));
                }

                var power = peak.Magnitude * peak.Magnitude;
                for (var i = 0; i < 12; i++)
                {
                    if (amplitude[i] > 0)
                        pcp[i] += window[i] * power * amplitude[i] * amplitude[i];
                }
            }
        }
    }
}
