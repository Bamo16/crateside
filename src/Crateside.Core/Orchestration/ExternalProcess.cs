using System.Diagnostics;
using System.Text;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Runs a command to completion and hands back what it said.
/// </summary>
public static class ExternalProcess
{
    public static async Task<ProcessOutcome> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default
    )
    {
        var info = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };

        using var process =
            Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {executable}");

        // Read both streams before waiting: a full pipe with nobody draining it blocks forever.
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Stop(process);

            throw;
        }

        return new ProcessOutcome
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdout,
            StandardError = await stderr,
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

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or SystemException)
        {
            // It exited on its own between the check and the kill. Nothing to do.
        }
    }
}

/// <summary>What a command did.</summary>
public sealed record ProcessOutcome
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }

    public bool Succeeded => ExitCode is 0;
}
