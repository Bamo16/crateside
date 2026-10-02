using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// The sidecars crateside wrote: a sibling's warp or a starter warp. One counts as crateside's only
/// while it is the file crateside wrote; once Live saves over it, the warp is the user's (ADR 0043).
/// </summary>
public sealed class WrittenWarps(string? path = null)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, WrittenWarp> _entries = Load(path);

    public static string DefaultPath { get; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "written-warps.json");

    /// <summary>The warp crateside wrote that this sample's sidecar still holds, or null if it holds the user's.</summary>
    public WrittenWarp? Of(string audioPath) => Current(audioPath);

    /// <summary>The audio file whose warp this sample's sidecar holds, or null if it holds none crateside borrowed.</summary>
    public string? BorrowedFrom(string audioPath) => Current(audioPath)?.From;

    /// <summary>Whether this sample's sidecar is still a starter warp crateside wrote, which is not a finished warp (#77).</summary>
    public bool IsStarter(string audioPath) => Current(audioPath) is { IsStarter: true };

    /// <summary>Whether this sample's sidecar is still one crateside wrote, and so crateside's to replace.</summary>
    public bool IsCratesides(string audioPath) => Current(audioPath) is not null;

    public void RecordBorrowed(string audioPath, string from) => Record(audioPath, from);

    public void RecordStarter(string audioPath) => Record(audioPath, null);

    private WrittenWarp? Current(string audioPath)
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

    private void Record(string audioPath, string? from)
    {
        var sidecar = new FileInfo(SidecarOf(audioPath));

        lock (_lock)
        {
            _entries[sidecar.FullName] = new WrittenWarp(
                from,
                sidecar.Length,
                sidecar.LastWriteTimeUtc
            );

            if (path is not null)
                WrittenWarpsJsonContext.Default.DictionaryStringWrittenWarp.WriteFile(
                    path,
                    _entries
                        .Where(pair => File.Exists(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                );
        }
    }

    private static string SidecarOf(string audioPath) =>
        Path.GetFullPath(StemFileName.AnalysisFor(audioPath));

    private static Dictionary<string, WrittenWarp> Load(string? path) =>
        new(
            path is not null
            && WrittenWarpsJsonContext.Default.DictionaryStringWrittenWarp.ReadFileOrDefault(path)
                is { } entries
                ? entries
                : [],
            StringComparer.OrdinalIgnoreCase
        );
}

/// <param name="From">The sibling's audio file, or null for a starter warp.</param>
/// <param name="Length">The sidecar's size as written, to tell whether Live has saved over it.</param>
/// <param name="Written">The sidecar's last write time (UTC) as written.</param>
public sealed record WrittenWarp(string? From, long Length, DateTime Written)
{
    [JsonIgnore]
    public bool IsStarter => From is null;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, WrittenWarp>))]
internal sealed partial class WrittenWarpsJsonContext : JsonSerializerContext { }
