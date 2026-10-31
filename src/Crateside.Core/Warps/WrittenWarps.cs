using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// The sidecars crateside wrote: a sibling's warp, a starter warp, or a clip with Warp off. One counts
/// as crateside's only while it is the file crateside wrote; once Live saves over it, it is the
/// user's (ADR 0043, ADR 0059).
/// </summary>
public sealed class WrittenWarps
{
    private readonly Lock _lock = new();
    private readonly string? _path;
    private readonly Dictionary<string, WrittenWarp> _entries;

    public WrittenWarps(CratesidePaths paths)
        : this(paths.WrittenWarpsFile) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal WrittenWarps(string? path = null)
    {
        _path = path;
        _entries = Load(path);
    }

    /// <summary>The warp crateside wrote that this sample's sidecar still holds, or null if it holds the user's.</summary>
    public WrittenWarp? Of(string audioPath) => Current(audioPath);

    /// <summary>The audio file whose warp this sample's sidecar holds, or null if it holds none crateside borrowed.</summary>
    public string? BorrowedFrom(string audioPath) => Current(audioPath)?.From;

    /// <summary>Whether this sample's sidecar is still a starter warp crateside wrote, which is not a finished warp (#77).</summary>
    public bool IsStarter(string audioPath) => Current(audioPath) is { IsStarter: true };

    /// <summary>Whether this sample's sidecar is still one crateside wrote, and so crateside's to replace.</summary>
    public bool IsCratesides(string audioPath) => Current(audioPath) is not null;

    /// <summary>The warp crateside last wrote into this sample's sidecar, even if Live or the user has saved over it since.</summary>
    public WrittenWarp? Ever(string audioPath)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(SidecarOf(audioPath));
        }
    }

    /// <summary>The samples whose sidecars still hold this sample's warp, lent by crateside.</summary>
    public IReadOnlyList<string> LentTo(string audioPath)
    {
        var lender = Path.GetFullPath(audioPath);
        List<string> sidecars;

        lock (_lock)
        {
            sidecars =
            [
                .. _entries
                    .Where(pair =>
                        pair.Value.From is { } from
                        && string.Equals(
                            Path.GetFullPath(from),
                            lender,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Select(pair => pair.Key),
            ];
        }

        return
        [
            .. sidecars
                .Select(sidecar => sidecar[..^".asd".Length])
                .Where(borrower => Current(borrower) is not null),
        ];
    }

    public void RecordBorrowed(string audioPath, string from) =>
        Record(audioPath, from, unwarped: false);

    public void RecordStarter(string audioPath) => Record(audioPath, null, unwarped: false);

    public void RecordUnwarped(string audioPath) => Record(audioPath, null, unwarped: true);

    /// <summary>Records a sidecar crateside rewrote as still being what it was.</summary>
    public void Rerecord(string audioPath, WrittenWarp was) =>
        Record(audioPath, was.From, was.IsUnwarped);

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

    private void Record(string audioPath, string? from, bool unwarped)
    {
        var sidecar = new FileInfo(SidecarOf(audioPath));

        lock (_lock)
        {
            _entries[sidecar.FullName] = new WrittenWarp(
                from,
                sidecar.Length,
                sidecar.LastWriteTimeUtc,
                unwarped
            );

            if (_path is not null)
                WrittenWarpsJsonContext.Default.DictionaryStringWrittenWarp.WriteFile(
                    _path,
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

/// <summary>A sidecar crateside wrote: a sibling's warp, a starter warp, or a clip with Warp off.</summary>
/// <param name="From">The sibling's audio file, or null for a starter warp.</param>
/// <param name="Length">The sidecar's size as written, to tell whether Live has saved over it.</param>
/// <param name="Written">The sidecar's last write time (UTC) as written.</param>
/// <param name="IsUnwarped">A clip with Warp off, for a stem that gets no starter: no warp at all (ADR 0059).</param>
public sealed record WrittenWarp(
    string? From,
    long Length,
    DateTime Written,
    bool IsUnwarped = false
)
{
    [JsonIgnore]
    public bool IsStarter => From is null && !IsUnwarped;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, WrittenWarp>))]
internal sealed partial class WrittenWarpsJsonContext : JsonSerializerContext { }
