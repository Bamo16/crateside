using System.Reflection;
using Crateside.Core.Pairing;
using Microsoft.ML.OnnxRuntime;

namespace Crateside.Core.KeyDetection;

/// <summary>
/// madmom's CNN key classifier (Korzeniowski and Widmer, ISMIR 2018), run through ONNX Runtime on
/// the CPU over a log-filtered spectrogram computed as madmom computes it (ADR 0055).
/// </summary>
public sealed class MadmomKeyCnn : IDisposable
{
    public const string Name = "madmom";

    /// <summary>The fewest spectrogram frames the net's three 2×2 pools leave anything of.</summary>
    private const int MinimumFrames = 8;

    private readonly Lazy<InferenceSession> _session = new(() => new InferenceSession(ReadModel()));

    /// <summary>Over 44.1 kHz mono samples.</summary>
    public KeyReading Read(ReadOnlySpan<float> samples)
    {
        var spectrogram = LogFilteredSpectrogram.Compute(samples);
        var frames = spectrogram.Length / LogFilteredSpectrogram.Bands;

        if (frames < MinimumFrames)
            return new KeyReading.Failed(
                $"too short for madmom: {frames} frames of the {MinimumFrames} it needs"
            );

        using var input = OrtValue.CreateTensorValueFromMemory(
            spectrogram,
            [1, 1, frames, LogFilteredSpectrogram.Bands]
        );
        using var runOptions = new RunOptions();
        using var output = _session.Value.Run(
            runOptions,
            new Dictionary<string, OrtValue> { ["spectrogram"] = input },
            _session.Value.OutputNames
        );

        var probabilities = output[0].GetTensorDataAsSpan<float>();
        var best = 0;
        for (var i = 1; i < probabilities.Length; i++)
        {
            if (probabilities[i] > probabilities[best])
                best = i;
        }

        return new KeyReading.Read(KeyOfClass(best), probabilities[best]);
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
            _session.Value.Dispose();
    }

    /// <summary>madmom's class order: the twelve majors from A, then the twelve minors from A.</summary>
    private static MusicKey KeyOfClass(int index) =>
        KeyReading.KeyOf((9 + index % 12) % 12, index < 12 ? Mode.Major : Mode.Minor);

    private static byte[] ReadModel()
    {
        using var stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream("madmom-key-cnn.onnx")
            ?? throw new InvalidOperationException("the madmom key model is not embedded");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }
}

/// <summary>
/// madmom's key front end: 8192-sample Hann frames at 5 per second, magnitude spectrum, triangular
/// filters 24 to the octave from 65 to 2100 Hz, then <c>log10(1 + x)</c>.
/// </summary>
internal static class LogFilteredSpectrogram
{
    public const int Bands = 105;

    private const int SampleRate = 44100;
    private const int FrameSize = 8192;
    private const double HopSize = SampleRate / 5.0;
    private const int Bins = FrameSize / 2;

    private static readonly double[] Window = HannSymmetric(FrameSize);

    private static readonly (int Start, float[] Weights)[] Filters = BuildFilters();

    /// <summary>Row-major, frame by frame, <see cref="Bands"/> values to a frame.</summary>
    public static float[] Compute(ReadOnlySpan<float> samples)
    {
        var frames = (int)Math.Ceiling(samples.Length / HopSize);
        var result = new float[frames * Bands];
        var fft = new Fft(FrameSize);
        var frame = new double[FrameSize];
        var magnitudes = new double[Bins];

        for (var f = 0; f < frames; f++)
        {
            // Centered on its sample, zero-padded past either end.
            var start = (int)(f * HopSize) - FrameSize / 2;
            for (var i = 0; i < FrameSize; i++)
            {
                var index = start + i;
                frame[i] = index >= 0 && index < samples.Length ? samples[index] * Window[i] : 0;
            }

            fft.Magnitudes(frame, magnitudes, Bins);

            for (var band = 0; band < Bands; band++)
            {
                var (first, weights) = Filters[band];
                var sum = 0.0;
                for (var i = 0; i < weights.Length; i++)
                    sum += (float)magnitudes[first + i] * weights[i];

                result[f * Bands + band] = (float)Math.Log10(sum + 1);
            }
        }

        return result;
    }

    private static double[] HannSymmetric(int size) =>
        [
            .. Enumerable
                .Range(0, size)
                .Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (size - 1))),
        ];

    private static (int Start, float[] Weights)[] BuildFilters()
    {
        var centers = CenterBins();
        var filters = new (int, float[])[centers.Length - 2];

        for (var i = 0; i + 2 < centers.Length; i++)
        {
            var (start, center, stop) = (centers[i], centers[i + 1], centers[i + 2]);

            if (stop - start < 2)
                (center, stop) = (start, start + 1);

            var weights = new double[stop - start];
            var rise = center - start;
            var fall = stop - center;
            for (var j = 0; j < rise; j++)
                weights[j] = (double)j / rise;
            for (var j = 0; j < fall; j++)
                weights[rise + j] = 1 - (double)j / fall;

            var total = weights.Sum();
            filters[i] = (start, [.. weights.Select(weight => (float)(weight / total))]);
        }

        return filters;
    }

    /// <summary>The bins nearest each log-spaced frequency, without repeats.</summary>
    private static int[] CenterBins()
    {
        const double reference = 440;
        const int perOctave = 24;
        var left = Math.Floor(Math.Log2(65 / reference) * perOctave);
        var right = Math.Ceiling(Math.Log2(2100 / reference) * perOctave);
        var frequencies = Enumerable
            .Range(0, (int)(right - left))
            .Select(i => reference * Math.Pow(2, (left + i) / perOctave))
            .Where(frequency => frequency is >= 65 and <= 2100);

        var binFrequencies = Enumerable
            .Range(0, Bins)
            .Select(k => k * (double)SampleRate / FrameSize)
            .ToArray();

        return
        [
            .. frequencies
                .Select(frequency => NearestBin(binFrequencies, frequency))
                .Distinct()
                .Order(),
        ];
    }

    /// <summary>Ties go to the upper bin, as madmom's <c>frequencies2bins</c> resolves them.</summary>
    private static int NearestBin(double[] binFrequencies, double frequency)
    {
        var index = Array.BinarySearch(binFrequencies, frequency);
        if (index < 0)
            index = ~index;
        index = Math.Clamp(index, 1, binFrequencies.Length - 1);

        return frequency - binFrequencies[index - 1] < binFrequencies[index] - frequency
            ? index - 1
            : index;
    }
}
