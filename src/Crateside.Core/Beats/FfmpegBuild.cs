using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Beats;

/// <summary>Which ffmpeg read a file: its version and the executable's hash, with the commit when a <c>build.json</c> beside it vouches for that hash.</summary>
public sealed partial record FfmpegBuild(string Version, string Sha256, string? Commit)
{
    public string Label => Commit is { Length: >= 7 } ? $"{Version}-{Commit[..7]}" : Version;

    [GeneratedRegex(
        @"^Filter beatdetect\r?\n(.*\(beatdetect (?<Version>[^)\s]+)\))?",
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

    /// <summary>Asks the executable for its beatdetect filter; null when it cannot be run or has none.</summary>
    public static async Task<FfmpegBuild?> IdentifyAsync(string fileName, CancellationToken ct)
    {
        ProcessOutcome outcome;
        try
        {
            outcome = await ExternalProcess.RunAsync(
                fileName,
                ["-hide_banner", "-h", "filter=beatdetect"],
                ct: ct
            );
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (
            outcome is not { Succeeded: true, StandardOutput: { } stdOut }
            || FilterHelp.Match(stdOut) is not { Success: true } line
            || Located(fileName) is not { Exists: true } executable
        )
            return null;

        var sha256 = await HashAsync(executable, ct);
        var buildJson = Path.Combine(executable.DirectoryName ?? string.Empty, "build.json");

        // A build.json vouches for the commit only when it records this binary's hash.
        var commit =
            BeatJsonContext.Default.BuildInfo.ReadFileOrDefault(buildJson)
                is { Sha256: { } recorded, Commit: { } recordedCommit }
            && recorded.Equals(sha256, StringComparison.OrdinalIgnoreCase)
                ? recordedCommit
                : null;

        // The filter reports its own version; builds before versioning say none.
        var version = line.Groups["Version"] is { Success: true, Value: var reported }
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
