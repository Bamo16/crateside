using System.Collections.Concurrent;
using System.Numerics;

namespace Crateside.Core.SourceOffsets;

/// <summary>An in-place radix-2 FFT over interleaved single-precision complex values, parallel across each stage.</summary>
internal static class Fft
{
    private const int ChunkButterflies = 1 << 16;

    private static readonly ConcurrentDictionary<int, float[]> Twiddles = new();

    /// <summary>Whether the transform of <paramref name="size"/> complex values is defined: a power of two.</summary>
    public static bool IsSize(int size) => size > 1 && BitOperations.IsPow2(size);

    /// <summary>The smallest transform size that holds <paramref name="length"/> values.</summary>
    public static int SizeFor(int length) => (int)BitOperations.RoundUpToPowerOf2((uint)length);

    /// <summary>
    /// Forward transform of <c>data</c>, re and im interleaved; the inverse is the same without the
    /// 1/n, so a round trip is n times what went in.
    /// </summary>
    public static void Transform(float[] data, bool inverse = false)
    {
        var size = data.Length / 2;
        var log2 = BitOperations.Log2((uint)size);
        var twiddles = Twiddles.GetOrAdd(size, MakeTwiddles);

        Reverse(data, log2);

        for (var stage = 1; stage <= log2; stage++)
            Stage(data, twiddles, stage, log2 - stage, inverse);
    }

    private static float[] MakeTwiddles(int size)
    {
        var table = new float[size];

        Cpu.For(
            0,
            size / 2,
            k =>
            {
                var (sin, cos) = Math.SinCos(-2 * Math.PI * k / size);

                table[2 * k] = (float)cos;
                table[(2 * k) + 1] = (float)sin;
            }
        );

        return table;
    }

    private static void Reverse(float[] data, int log2)
    {
        var size = 1 << log2;
        var shift = 32 - log2;

        Cpu.For(
            0,
            (size + ChunkButterflies - 1) / ChunkButterflies,
            chunk =>
            {
                var end = Math.Min(size, (chunk + 1) * ChunkButterflies);

                for (var i = chunk * ChunkButterflies; i < end; i++)
                {
                    var j = (int)(ReverseBits((uint)i) >> shift);

                    if (j <= i)
                        continue;

                    (data[2 * i], data[2 * j]) = (data[2 * j], data[2 * i]);
                    (data[(2 * i) + 1], data[(2 * j) + 1]) = (data[(2 * j) + 1], data[(2 * i) + 1]);
                }
            }
        );
    }

    private static uint ReverseBits(uint value)
    {
        value = ((value >> 1) & 0x55555555) | ((value & 0x55555555) << 1);
        value = ((value >> 2) & 0x33333333) | ((value & 0x33333333) << 2);
        value = ((value >> 4) & 0x0F0F0F0F) | ((value & 0x0F0F0F0F) << 4);
        value = ((value >> 8) & 0x00FF00FF) | ((value & 0x00FF00FF) << 8);

        return (value >> 16) | (value << 16);
    }

    private static void Stage(float[] data, float[] twiddles, int stage, int stride, bool inverse)
    {
        var half = 1 << (stage - 1);
        var butterflies = data.Length / 4;
        var sign = inverse ? -1f : 1f;

        Cpu.For(
            0,
            (butterflies + ChunkButterflies - 1) / ChunkButterflies,
            chunk =>
            {
                var end = Math.Min(butterflies, (chunk + 1) * ChunkButterflies);

                for (var b = chunk * ChunkButterflies; b < end; b++)
                {
                    var j = b & (half - 1);
                    var upper = (((b >> (stage - 1)) << stage) + j) * 2;
                    var lower = upper + (half * 2);
                    var t = (j << stride) * 2;
                    float wr = twiddles[t],
                        wi = sign * twiddles[t + 1];
                    var vr = (data[lower] * wr) - (data[lower + 1] * wi);
                    var vi = (data[lower] * wi) + (data[lower + 1] * wr);

                    data[lower] = data[upper] - vr;
                    data[lower + 1] = data[upper + 1] - vi;
                    data[upper] += vr;
                    data[upper + 1] += vi;
                }
            }
        );
    }
}
