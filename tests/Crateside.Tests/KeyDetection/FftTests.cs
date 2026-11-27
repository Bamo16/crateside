using Crateside.Core.KeyDetection;

namespace Crateside.Tests.KeyDetection;

public sealed class FftTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void A_size_that_is_not_a_power_of_two_is_refused(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fft(size));
    }

    [Theory]
    [InlineData(64, 5)]
    [InlineData(256, 37)]
    public void A_sine_lands_in_its_own_bin_with_half_its_amplitude_times_the_size(
        int size,
        int bin
    )
    {
        var signal = Enumerable
            .Range(0, size)
            .Select(i => 2 * Math.Sin(2 * Math.PI * bin * i / size))
            .ToArray();
        var magnitudes = new double[size / 2 + 1];

        new Fft(size).Magnitudes(signal, magnitudes, magnitudes.Length);

        Assert.Equal(size, magnitudes[bin], 6);
        Assert.All(
            magnitudes.Where((_, index) => index != bin),
            magnitude => Assert.True(magnitude < 1e-6, $"leaked {magnitude}")
        );
    }

    [Fact]
    public void A_constant_signal_is_all_in_bin_zero()
    {
        var magnitudes = new double[5];

        new Fft(8).Magnitudes(Enumerable.Repeat(0.5, 8).ToArray(), magnitudes, 5);

        Assert.Equal(4, magnitudes[0], 9);
        Assert.All(magnitudes.Skip(1), magnitude => Assert.True(magnitude < 1e-9));
    }

    [Fact]
    public void A_single_impulse_has_a_flat_spectrum()
    {
        double[] signal = [1, 0, 0, 0, 0, 0, 0, 0];
        var magnitudes = new double[5];

        new Fft(8).Magnitudes(signal, magnitudes, 5);

        Assert.All(magnitudes, magnitude => Assert.Equal(1, magnitude, 9));
    }

    [Fact]
    public void Only_the_asked_for_bins_are_written()
    {
        double[] magnitudes = [-1, -1, -1, -1];

        new Fft(8).Magnitudes(Enumerable.Repeat(1.0, 8).ToArray(), magnitudes, 2);

        Assert.Equal([8, 0, -1, -1], magnitudes.Select(m => Math.Round(m, 9)));
    }
}
