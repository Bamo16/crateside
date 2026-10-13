using System.Text;
using CliWrap;
using CliWrap.Buffered;

namespace Crateside.Core.Orchestration.Loop;

/// <summary>
/// Runs a command to completion and hands back what it said.
/// </summary>
public static class ExternalProcess
{
    public static async Task<ProcessOutcome> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default
    )
    {
        var result = await Cli.Wrap(fileName)
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory ?? string.Empty)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(Encoding.UTF8, ct);

        return new ProcessOutcome
        {
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
        };
    }

    /// <summary>
    /// The last non-blank line a failing command said, stderr before stdout, for a human to read.
    /// </summary>
    public static string? Describe(params string[] streams) =>
        streams
            .Select(s => s.Trim())
            .FirstOrDefault(s => s is { Length: > 0 })
            ?.Split('\n')
            .LastOrDefault(l => !l.AsSpan().IsWhiteSpace())
            ?.Trim();
}

/// <summary>What a command did.</summary>
public sealed record ProcessOutcome
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }

    public bool Succeeded => ExitCode is 0;
}
