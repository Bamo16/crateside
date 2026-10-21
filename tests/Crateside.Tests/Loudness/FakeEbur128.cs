using System.Globalization;
using Crateside.Core.Loudness;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Loudness;

/// <summary>Prints the summary ffmpeg's <c>ebur128</c> ends with, at the loudness given for each file.</summary>
internal sealed class FakeEbur128(Func<string, double> lufsOf) : IProcessRunner
{
    public int Runs { get; private set; }

    public static ClipGains Gains(Func<string, double> lufsOf, LoudnessOptions? options = null) =>
        new(
            new LoudnessReader(new FakeEbur128(lufsOf)),
            new CurrentOptions<LoudnessOptions>(() => options ?? new())
        );

    public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        Runs++;
        var file = request.Arguments[request.Arguments.ToList().IndexOf("-i") + 1];

        return Task.FromResult(
            new ProcessOutcome
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = Summary(lufsOf(file)),
            }
        );
    }

    public static string Summary(double lufs) =>
        $"""
            [Parsed_ebur128_0 @ 0000020d8a11ec00] Summary:

              Integrated loudness:
                I:         {lufs.ToString("0.0", CultureInfo.InvariantCulture)} LUFS
                Threshold: -24.5 LUFS

              Loudness range:
                LRA:        11.1 LU
                Threshold: -34.8 LUFS
            """;
}
