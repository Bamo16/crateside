namespace Crateside.Core.SourceOffsets;

/// <summary>Half the machine's cores for the measurement, so it can run unattended beside the user's work.</summary>
internal static class Cpu
{
    private static readonly ParallelOptions Options = new()
    {
        MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
    };

    public static void For(int from, int to, Action<int> body) =>
        Parallel.For(from, to, Options, body);
}
