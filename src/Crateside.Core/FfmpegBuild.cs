using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core;

/// <summary>The ffmpeg filters crateside pins a build for, each on its own (#102, ADR 0066).</summary>
public enum FfmpegFilter
{
    Beatdetect,
    Keydetect,
}

/// <summary>Which ffmpeg read a file: its filter's version and the executable's hash, with the commit when a <c>build.json</c> beside it vouches for that hash.</summary>
public sealed partial record FfmpegBuild(string Version, string Sha256, string? Commit)
{
    public string Label => Commit is { Length: >= 7 } ? $"{Version}-{Commit[..7]}" : Version;

    [GeneratedRegex(
        @"^Filter (?<Filter>\w+)\r?\n(.*\((?<Reporter>\w+) (?<Version>[^)\s]+)\))?",
        RegexOptions.Multiline
    )]
    private static partial Regex FilterHelp { get; }

    [GeneratedRegex(@"(?<Number>\d+\.\d+\.\d+)$")]
    private static partial Regex TrailingNumber { get; }

    /// <summary>Whether the filter's version is below <paramref name="other"/>'s; an unversioned build is older than any versioned one.</summary>
    public bool IsOlderThan(FfmpegBuild other) =>
        (NumberOf(Version), NumberOf(other.Version)) switch
        {
            (null, not null) => true,
            ({ } mine, { } theirs) => mine < theirs,
            _ => false,
        };

    /// <summary>The filter's name on ffmpeg's command line.</summary>
    public static string NameOf(FfmpegFilter filter) =>
        filter switch
        {
            FfmpegFilter.Beatdetect => "beatdetect",
            FfmpegFilter.Keydetect => "keydetect",
            _ => throw new UnreachableException(),
        };

    /// <summary>Asks the executable for the filter; null when it cannot be run or has no such filter.</summary>
    public static async Task<FfmpegBuild?> IdentifyAsync(
        IProcessRunner processes,
        string fileName,
        FfmpegFilter filter,
        CancellationToken ct
    )
    {
        var name = NameOf(filter);
        ProcessOutcome outcome;
        try
        {
            outcome = await processes.RunAsync(
                new ProcessRequest(fileName, ["-hide_banner", "-h", $"filter={name}"]),
                ct
            );
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (
            outcome is not { Succeeded: true, StandardOutput: { } stdOut }
            || FilterHelp.Match(stdOut) is not { Success: true } line
            || line.Groups["Filter"].Value != name
            || Located(fileName) is not { Exists: true } executable
        )
            return null;

        var sha256 = await HashAsync(executable, ct);
        var buildJson = Path.Combine(executable.DirectoryName ?? string.Empty, "build.json");

        // A build.json vouches for the commit only when it records this binary's hash.
        var commit =
            BuildsJsonContext.Default.BuildInfo.ReadFileOrDefault(buildJson)
                is { Sha256: { } recorded, Commit: { } recordedCommit }
            && recorded.Equals(sha256, StringComparison.OrdinalIgnoreCase)
                ? recordedCommit
                : null;

        // The filter reports its own version; builds before versioning say none.
        var version =
            line.Groups["Reporter"].Value == name
            && line.Groups["Version"] is { Success: true, Value: var reported }
                ? reported
                : "unversioned";

        return new FfmpegBuild(version, sha256, commit);
    }

    private static System.Version? NumberOf(string version) =>
        TrailingNumber.Match(version).Groups["Number"] is { Success: true, Value: var number }
            ? System.Version.Parse(number)
            : null;

    internal static FileInfo? Located(string fileName) =>
        Path.IsPathRooted(fileName)
            ? new FileInfo(fileName)
            : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => new FileInfo(
                    Path.Combine(
                        folder,
                        OperatingSystem.IsWindows() && !Path.HasExtension(fileName)
                            ? $"{fileName}.exe"
                            : fileName
                    )
                ))
                .FirstOrDefault(candidate => candidate.Exists);

    internal static async Task<string> HashAsync(FileInfo executable, CancellationToken ct)
    {
        await using var stream = executable.OpenRead();

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }
}

internal sealed record BuildInfo(string? Commit, string? Sha256);
