using Crateside.Core;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests;

/// <summary>Stands in for ffmpeg builds made as files under a folder: each answers for its own filter, and a run prints nothing.</summary>
internal sealed class FakeFfmpegBuilds(string folder) : IProcessRunner
{
    private readonly Dictionary<string, (string Filter, string Version)> _builds = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>A new executable in a folder of its own, carrying the filter at that version, its bytes unlike any other's.</summary>
    public string Add(FfmpegFilter filter, string version)
    {
        var path = Path.Combine(
            Directory.CreateDirectory(Path.Combine(folder, Guid.NewGuid().ToString("n"))).FullName,
            "ffmpeg.exe"
        );
        File.WriteAllText(path, path);
        _builds[path] = (FfmpegBuild.NameOf(filter), version);

        return path;
    }

    public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
        Task.FromResult(
            request.Arguments switch
            {
                [.., "-h", var asked] => _builds.TryGetValue(request.FileName, out var build)
                && asked == $"filter={build.Filter}"
                    ? Outcome(
                        0,
                        $"Filter {build.Filter}\n  A fake ({build.Filter} {build.Version}).\n"
                    )
                    : Outcome(1, string.Empty),
                _ => Outcome(0, string.Empty),
            }
        );

    private static ProcessOutcome Outcome(int exitCode, string standardOutput) =>
        new()
        {
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = string.Empty,
        };
}
