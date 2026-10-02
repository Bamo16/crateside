using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// The sidecars crateside wrote with a sibling's warp. One counts as borrowed only while it is the
/// file crateside wrote; once Live saves over it, the warp is the user's (ADR 0043).
/// </summary>
public sealed class BorrowedWarps(string? path = null)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, BorrowedWarp> _entries = Load(path);

    public static string DefaultPath { get; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "borrowed-warps.json");

    /// <summary>The audio file whose warp this sample's sidecar holds, or null if the user's own.</summary>
    public string? BorrowedFrom(string audioPath)
    {
        var sidecar = SidecarOf(audioPath);

        lock (_lock)
        {
            return
                _entries.TryGetValue(sidecar, out var entry)
                && new FileInfo(sidecar) is { Exists: true } file
                && entry.Length == file.Length
                && entry.Written == file.LastWriteTimeUtc
                ? entry.From
                : null;
        }
    }

    public void Record(string audioPath, string from)
    {
        var sidecar = new FileInfo(SidecarOf(audioPath));

        lock (_lock)
        {
            _entries[sidecar.FullName] = new BorrowedWarp(
                from,
                sidecar.Length,
                sidecar.LastWriteTimeUtc
            );

            if (path is not null)
                BorrowedWarpsJsonContext.Default.DictionaryStringBorrowedWarp.WriteFile(
                    path,
                    _entries
                        .Where(pair => File.Exists(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                );
        }
    }

    private static string SidecarOf(string audioPath) =>
        Path.GetFullPath(StemFileName.AnalysisFor(audioPath));

    private static Dictionary<string, BorrowedWarp> Load(string? path) =>
        new(
            path is not null
            && BorrowedWarpsJsonContext.Default.DictionaryStringBorrowedWarp.ReadFileOrDefault(path)
                is { } entries
                ? entries
                : [],
            StringComparer.OrdinalIgnoreCase
        );
}

/// <param name="From">The sibling's audio file.</param>
/// <param name="Length">The sidecar's size as written, to tell whether Live has saved over it.</param>
/// <param name="Written">The sidecar's last write time (UTC) as written.</param>
public sealed record BorrowedWarp(string From, long Length, DateTime Written);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, BorrowedWarp>))]
internal sealed partial class BorrowedWarpsJsonContext : JsonSerializerContext { }
