namespace Crateside.Core.Tuning;

/// <summary>What a track's tuning readings decide: a detune to write, a track to review, or nothing (ADR 0101).</summary>
public abstract record TuningVerdict
{
    public const double SteadyWithin = 6;
    public const double WriteFrom = 10;
    public const double ReviewAbove = 30;
    public const int SteadyReadings = 3;

    private TuningVerdict() { }

    /// <param name="Median">The tuning read, in cents from A = 440.</param>
    /// <param name="Correction">The detune to write: the median negated.</param>
    public sealed record Write(double Median, double Correction) : TuningVerdict;

    public sealed record Review(double? Median, string Why) : TuningVerdict;

    public sealed record InTune(double Median) : TuningVerdict;

    public static TuningVerdict Of(IReadOnlyList<double> readings)
    {
        if (readings is [])
            return new Review(null, "unsteady");

        var median = MedianOf(readings);

        if (readings.Count < SteadyReadings || readings.Max() - readings.Min() > SteadyWithin)
            return readings.Any(reading => Math.Abs(reading) >= WriteFrom)
                ? new Review(median, "unsteady")
                : new InTune(median);

        return Math.Abs(median) switch
        {
            < WriteFrom => new InTune(median),
            > ReviewAbove => new Review(median, "near a quarter tone"),
            _ => new Write(median, -median),
        };
    }

    private static double MedianOf(IReadOnlyList<double> readings) =>
        readings.Order().ToArray() is var sorted && sorted.Length % 2 is 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
}
