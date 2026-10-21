using System.Text;
using CliWrap;
using CliWrap.Buffered;

namespace Crateside.Core.Orchestration.Loop;

/// <summary>Runs a command to completion: the seam a test replaces.</summary>
public interface IProcessRunner
{
    Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default);
}

/// <summary>A command to run, with what it is given beyond its arguments.</summary>
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }

    /// <summary>Variables set for the process on top of crateside's own environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>();

    public string? StandardInput { get; init; }
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(
        ProcessRequest request,
        CancellationToken ct = default
    )
    {
        var command = Cli.Wrap(request.FileName)
            .WithArguments(request.Arguments)
            .WithWorkingDirectory(request.WorkingDirectory ?? string.Empty)
            .WithEnvironmentVariables(
                request.Environment.ToDictionary(pair => pair.Key, string? (pair) => pair.Value)
            )
            .WithValidation(CommandResultValidation.None);

        if (request.StandardInput is { } input)
            command = command.WithStandardInputPipe(PipeSource.FromString(input, Encoding.UTF8));

        var result = await command.ExecuteBufferedAsync(Encoding.UTF8, ct);

        return new ProcessOutcome
        {
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
        };
    }
}

/// <summary>What a command did.</summary>
public sealed record ProcessOutcome
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }

    public bool Succeeded => ExitCode is 0;

    /// <summary>The last non-blank line it said, stderr before stdout, for a human to read.</summary>
    public string? LastLine =>
        new[] { StandardError, StandardOutput }
            .Select(stream => stream.Trim())
            .FirstOrDefault(stream => stream is { Length: > 0 })
            ?.Split('\n')
            .LastOrDefault(line => !line.AsSpan().IsWhiteSpace())
            ?.Trim();
}
