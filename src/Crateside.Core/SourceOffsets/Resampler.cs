namespace Crateside.Core.SourceOffsets;

/// <summary>Windowed-sinc resampling by a ratio, for reading a known speed change before refusing a source.</summary>
internal static class Resampler
{
    private const int ZeroCrossings = 16;
    private const int Steps = 1024;
    private const double KaiserBeta = 8.6;

    /// <summary>The input as played <paramref name="up"/>/<paramref name="down"/> as long: out[m] reads in at m * down / up.</summary>
    public static float[] Resample(float[] input, int up, int down)
    {
        var length = (int)Math.Ceiling((double)input.Length * up / down);
        var output = new float[length];
        var step = (double)down / up;
        var cutoff = Math.Min(1, 1 / step);
        var table = Kernel();
        var reach = (int)Math.Ceiling(ZeroCrossings / cutoff);
        const int chunk = 1 << 16;

        Cpu.For(
            0,
            (length + chunk - 1) / chunk,
            part =>
            {
                for (var m = part * chunk; m < Math.Min(length, (part + 1) * chunk); m++)
                {
                    var at = m * step;
                    var centre = (int)Math.Floor(at);
                    var sum = 0.0;

                    for (
                        var n = Math.Max(0, centre - reach + 1);
                        n <= Math.Min(input.Length - 1, centre + reach);
                        n++
                    )
                    {
                        var x = Math.Abs(((n - at) * Steps) * cutoff);
                        var index = (int)Math.Round(x);

                        if (index < table.Length)
                            sum += input[n] * table[index];
                    }

                    output[m] = (float)(sum * cutoff);
                }
            }
        );

        return output;
    }

    /// <summary>The kernel at 1/<see cref="Steps"/> of a (scaled) sample.</summary>
    private static float[] Kernel()
    {
        var table = new float[(ZeroCrossings * Steps) + 1];

        for (var i = 0; i < table.Length; i++)
        {
            var x = (double)i / Steps;
            var sinc = x is 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
            var ratio = x / ZeroCrossings;

            table[i] = (float)(
                sinc
                * BesselI0(KaiserBeta * Math.Sqrt(Math.Max(0, 1 - (ratio * ratio))))
                / BesselI0(KaiserBeta)
            );
        }

        return table;
    }

    private static double BesselI0(double x)
    {
        var sum = 1.0;
        var term = 1.0;

        for (var k = 1; k < 50; k++)
        {
            term *= x * x / (4.0 * k * k);
            sum += term;
        }

        return sum;
    }
}
