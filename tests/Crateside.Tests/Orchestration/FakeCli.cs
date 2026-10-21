namespace Crateside.Tests.Orchestration;

/// <summary>A stand-in for a command-line tool: a <c>.cmd</c> on Windows, a shell script elsewhere.</summary>
internal static class FakeCli
{
    public static string Printing(string directory, string name, string stdout, int exitCode = 0) =>
        Write(
            directory,
            name,
            windows: $"@echo off{Environment.NewLine}echo {stdout}{Environment.NewLine}exit /b {exitCode}",
            unix: $"cat <<'OUT'\n{stdout}\nOUT\nexit {exitCode}"
        );

    public static string Write(string directory, string name, string windows, string unix)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, $"{name}.cmd");

            File.WriteAllText(script, windows);

            return script;
        }

        var path = Path.Combine(directory, $"{name}.sh");

        File.WriteAllText(path, $"#!/bin/sh\n{unix}\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );

        return path;
    }
}
