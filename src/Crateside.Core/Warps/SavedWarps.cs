using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// The sidecars the user saved from the warp view, and the edits that save made (ADR 0107). A record
/// stands only while the sidecar is the file crateside wrote; once Live saves over it, it is Live's.
/// </summary>
public sealed class SavedWarps
{
    private readonly Lock _lock = new();
    private readonly string? _path;
    private readonly Dictionary<string, SavedWarp> _entries;

    public SavedWarps(CratesidePaths paths)
        : this(paths.SavedWarpsFile) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal SavedWarps(string? path = null)
    {
        _path = path;
        _entries = Load(path);
    }

    /// <summary>The save this sample's sidecar still holds, or null once Live has saved over it.</summary>
    public SavedWarp? Of(string audioPath)
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

    public void Record(string audioPath, SavedEdits edits, DateTimeOffset at)
    {
        var sidecar = new FileInfo(SidecarOf(audioPath));

        Change(entries =>
            entries[sidecar.FullName] = new SavedWarp(
                at,
                sidecar.Length,
                sidecar.LastWriteTimeUtc,
                edits
            )
        );
    }

    public void Forget(string audioPath) => Change(entries => entries.Remove(SidecarOf(audioPath)));

    private void Change(Action<Dictionary<string, SavedWarp>> change)
    {
        lock (_lock)
        {
            change(_entries);

            if (_path is not null)
                SavedWarpsJsonContext.Default.DictionaryStringSavedWarp.WriteFile(
                    _path,
                    _entries
                        .Where(pair => File.Exists(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                );
        }
    }

    private static string SidecarOf(string audioPath) =>
        Path.GetFullPath(StemFileName.AnalysisFor(audioPath));

    private static Dictionary<string, SavedWarp> Load(string? path) =>
        new(
            path is not null
            && SavedWarpsJsonContext.Default.DictionaryStringSavedWarp.ReadFileOrDefault(path)
                is { } entries
                ? entries
                : [],
            StringComparer.OrdinalIgnoreCase
        );
}

/// <summary>A warp saved from the warp view, and the sidecar as it was written.</summary>
/// <param name="Length">The sidecar's size as written, to tell whether Live has saved over it.</param>
/// <param name="Written">The sidecar's last write time (UTC) as written.</param>
public sealed record SavedWarp(DateTimeOffset At, long Length, DateTime Written, SavedEdits Edits);

/// <summary>What a save changed in the warp it started from.</summary>
/// <param name="BarShift">How many beats later the grid sits under the audio; negative for earlier.</param>
/// <param name="TempoFactor">The tempo's net scaling: 0.5 for halved, 2 for doubled, 1 for none.</param>
/// <param name="ClipStartBar">The bar the clip now starts on, or null when it stayed.</param>
/// <param name="ClipWasBar">The bar the clip started on before the save.</param>
/// <param name="FromStarter">Whether the warp it started from was crateside's starter.</param>
public sealed record SavedEdits(
    int BarShift,
    double NudgeMs,
    double TempoFactor,
    int? ClipStartBar,
    int? ClipWasBar,
    bool FromStarter
)
{
    [JsonIgnore]
    public bool IsEmpty =>
        BarShift is 0 && NudgeMs is 0 && Math.Abs(TempoFactor - 1) < 1e-9 && ClipStartBar is null;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, SavedWarp>))]
internal sealed partial class SavedWarpsJsonContext : JsonSerializerContext { }
