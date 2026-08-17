using System.Diagnostics;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Runs a command to completion and hands back what it said.
/// </summary>
/// <remarks>
/// For the stages that shell out and simply wait. <see cref="SeparationRunner"/> deliberately
/// does not use this: it has to claim the interlock between starting and waiting, and kill the
/// child on cancellation, so its process lifetime is the thing it is managing rather than an
/// implementation detail.
/// </remarks>
public static class ExternalProcess
{
    /// <param name="workingDirectory">
    /// Where to run it. Supplied when the command's own discovery depends on where it starts —
    /// a headless agent session finds its skills relative to the directory it opens in.
    /// </param>
    public static async Task<ProcessOutcome> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default
    )
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process =
            Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {executable}");

        // Read both streams before waiting: a child that fills one pipe's buffer blocks forever
        // if nobody is draining it, and these produce real volumes of log output.
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
    /// The last thing a failing command said, for an error message a human reads. Prefers
    /// stderr, falls back to stdout, and takes the last non-blank line — a stack trace's useful
    /// end rather than its start.
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
