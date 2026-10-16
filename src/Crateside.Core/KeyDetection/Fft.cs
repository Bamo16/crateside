using System.Numerics;

namespace Crateside.Core.KeyDetection;

/// <summary>An iterative radix-2 FFT over a power-of-two length, in double precision.</summary>
internal sealed class Fft
{
    private readonly int _size;
    private readonly int[] _reversed;
    private readonly Complex[] _twiddles;

    public Fft(int size)
    {
        if (size < 2 || !BitOperations.IsPow2(size))
            throw new ArgumentOutOfRangeException(nameof(size), size, "must be a power of two");

        _size = size;
        _reversed = new int[size];

        var bits = BitOperations.Log2((uint)size);
        for (var i = 0; i < size; i++)
            _reversed[i] = (int)(ReverseBits((uint)i) >> (32 - bits));

        _twiddles = new Complex[size / 2];
        for (var i = 0; i < size / 2; i++)
            _twiddles[i] = Complex.FromPolarCoordinates(1, -2 * Math.PI * i / size);
    }

    /// <summary>The magnitudes of bins 0 to size/2 of a real signal's spectrum.</summary>
    public void Magnitudes(ReadOnlySpan<double> signal, Span<double> magnitudes, int bins)
    {
        var data = Transform(signal);

        for (var k = 0; k < bins; k++)
            magnitudes[k] = data[k].Magnitude;
    }

    private Complex[] Transform(ReadOnlySpan<double> signal)
    {
        var data = new Complex[_size];
        for (var i = 0; i < _size; i++)
            data[_reversed[i]] = signal[i];

        for (var length = 2; length <= _size; length <<= 1)
        {
            var half = length / 2;
            var stride = _size / length;

            for (var start = 0; start < _size; start += length)
            {
                for (var k = 0; k < half; k++)
                {
                    var odd = data[start + k + half] * _twiddles[k * stride];
                    data[start + k + half] = data[start + k] - odd;
                    data[start + k] += odd;
                }
            }
        }

        return data;
    }

    private static uint ReverseBits(uint value)
    {
        value = ((value >> 1) & 0x55555555) | ((value & 0x55555555) << 1);
        value = ((value >> 2) & 0x33333333) | ((value & 0x33333333) << 2);
        value = ((value >> 4) & 0x0F0F0F0F) | ((value & 0x0F0F0F0F) << 4);
        value = ((value >> 8) & 0x00FF00FF) | ((value & 0x00FF00FF) << 8);

        return (value >> 16) | (value << 16);
    }
}
