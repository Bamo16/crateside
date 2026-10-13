using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// The sidecars whose warp crateside retyped to a truer tempo (#99). The warp stays the user's,
/// so it still lends and is still never replaced; this only remembers that crateside last wrote it.
/// </summary>
public sealed class RetypedWarps(string? path = null)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, RetypedWarp> _entries = Load(path);

    public static string DefaultPath { get; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "retyped-warps.json");

    /// <summary>The retype this sample's sidecar still holds, or null once Live or the user has saved over it.</summary>
    public RetypedWarp? Of(string audioPath)
    {
        var sidecar = SidecarOf(audioPath);

        lock (_lock)
        {
            return
                _entries.TryGetValue(sidecar, out var entry)
                && new FileInfo(sidecar) is { Exists: true } file
                && entry.Length == file.Length
                && entry.Written == file.LastWriteTimeUtc
                ? entry
                : null;
        }
    }

    public void Record(string audioPath, double bpm, DateTimeOffset at)
    {
        var sidecar = new FileInfo(SidecarOf(audioPath));

        lock (_lock)
        {
            _entries[sidecar.FullName] = new RetypedWarp(
                bpm,
                sidecar.Length,
                sidecar.LastWriteTimeUtc,
                at
            );

            if (path is not null)
                RetypedWarpsJsonContext.Default.DictionaryStringRetypedWarp.WriteFile(
                    path,
                    _entries
                        .Where(pair => File.Exists(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                );
        }
    }

    private static string SidecarOf(string audioPath) =>
        Path.GetFullPath(StemFileName.AnalysisFor(audioPath));

    private static Dictionary<string, RetypedWarp> Load(string? path) =>
        new(
            path is not null
            && RetypedWarpsJsonContext.Default.DictionaryStringRetypedWarp.ReadFileOrDefault(path)
                is { } entries
                ? entries
                : [],
            StringComparer.OrdinalIgnoreCase
        );
}

/// <param name="Length">The sidecar's size as written, to tell whether Live has saved over it.</param>
/// <param name="Written">The sidecar's last write time (UTC) as written.</param>
public sealed record RetypedWarp(double Bpm, long Length, DateTime Written, DateTimeOffset At);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, RetypedWarp>))]
internal sealed partial class RetypedWarpsJsonContext : JsonSerializerContext { }
