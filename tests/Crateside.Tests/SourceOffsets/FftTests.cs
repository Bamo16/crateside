using Crateside.Core.SourceOffsets;

namespace Crateside.Tests.SourceOffsets;

public sealed class FftTests
{
    [Fact]
    public void A_forward_transform_followed_by_an_inverse_gives_the_input_back()
    {
        var random = new Random(7);
        var original = new float[2 * 4096];

        for (var i = 0; i < original.Length; i++)
            original[i] = (float)random.NextDouble() - 0.5f;

        var data = (float[])original.Clone();

        Fft.Transform(data);
        Fft.Transform(data, inverse: true);

        for (var i = 0; i < original.Length; i++)
            Assert.Equal(original[i], data[i] / 4096, 1e-4);
    }

    [Fact]
    public void A_pure_tone_lands_in_its_own_bin()
    {
        const int size = 1024;
        const int bin = 37;
        var data = new float[2 * size];

        for (var i = 0; i < size; i++)
            data[2 * i] = (float)Math.Cos(2 * Math.PI * bin * i / size);

        Fft.Transform(data);
        var magnitudes = Enumerable
            .Range(0, size)
            .Select(i =>
                Math.Sqrt((data[2 * i] * data[2 * i]) + (data[(2 * i) + 1] * data[(2 * i) + 1]))
            )
            .ToArray();

        Assert.Equal(bin, Array.IndexOf(magnitudes, magnitudes.Take(size / 2).Max()));
        Assert.Equal(size / 2.0, magnitudes[bin], 0.5);
    }
}
