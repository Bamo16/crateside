using System.Text.Json.Serialization;
using AbleKit.Analysis;
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
        Record(audioPath, from, unwarped: false, starter: null);

    /// <param name="starter">Its grid and why its marker got its beat; null where that is not known.</param>
    public void RecordStarter(string audioPath, WrittenStarter? starter = null) =>
        Record(audioPath, null, unwarped: false, starter);

    public void RecordUnwarped(string audioPath) =>
        Record(audioPath, null, unwarped: true, starter: null);

    /// <summary>Records a sidecar crateside rewrote as still being what it was.</summary>
    public void Rerecord(string audioPath, WrittenWarp was) =>
        Record(audioPath, was.From, was.IsUnwarped, was.Starter);

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

    private void Record(string audioPath, string? from, bool unwarped, WrittenStarter? starter)
    {
        var sidecar = new FileInfo(SidecarOf(audioPath));

        lock (_lock)
        {
            _entries[sidecar.FullName] = new WrittenWarp(
                from,
                sidecar.Length,
                sidecar.LastWriteTimeUtc,
                unwarped,
                starter
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
/// <param name="Starter">A starter's grid and why its marker got its beat; null for anything else, and for starters written before #176.</param>
public sealed record WrittenWarp(
    string? From,
    long Length,
    DateTime Written,
    bool IsUnwarped = false,
    WrittenStarter? Starter = null
)
{
    [JsonIgnore]
    public bool IsStarter => From is null && !IsUnwarped;
}

/// <summary>A starter as written: its tempo and marker, the reading they came from, and why the marker got its beat (#176).</summary>
/// <param name="Bpm">The tempo written: the reading's, refined on the transients (ADR 0050).</param>
/// <param name="ReadBpm">The reading's tempo before it was refined.</param>
/// <param name="MarkerBeat">The marker's beat within its bar, 0 being the downbeat.</param>
/// <param name="ReadBeatSeconds">The reading's beat, which picked the 16th the transients' fold placed the marker in; null when the fold chose alone.</param>
/// <param name="Downbeat">The reading's downbeat, used or not; null when it read none.</param>
/// <param name="DownbeatSureAt">The confidence the downbeat had to reach to be used, when this was written.</param>
public sealed record WrittenStarter(
    double Bpm,
    double ReadBpm,
    double MarkerSeconds,
    double MarkerBeat,
    double? ReadBeatSeconds,
    ReadDownbeat? Downbeat,
    StarterBar BarFrom,
    double DownbeatSureAt
)
{
    public StarterBeat BeatFrom =>
        ReadBeatSeconds is null ? StarterBeat.FoldAlone : StarterBeat.ReadingRefinedByFold;

    /// <summary>The marker's beat within its bar counting from the reading's downbeat, used or not; null without one.</summary>
    public double? ReadMarkerBeat =>
        Downbeat is { Seconds: var downbeat }
            ? StarterGrid.BeatInBar(MarkerSeconds, downbeat, Bpm)
            : null;

    public static WrittenStarter Of(StarterGrid grid, double bpm, WarpMarker marker) =>
        new(
            bpm,
            grid.Bpm,
            marker.Seconds,
            marker.Beat,
            grid.BeatSeconds,
            grid.Read,
            grid switch
            {
                { DownbeatSeconds: not null } => StarterBar.Downbeat,
                { Read: null } => StarterBar.NoDownbeat,
                { Read.Confidence: not >= StarterGrid.DownbeatSureAt } => StarterBar.UnsureDownbeat,
                _ => StarterBar.NotInFours,
            },
            StarterGrid.DownbeatSureAt
        );
}

/// <summary>Where a starter's marker took its beat from.</summary>
public enum StarterBeat
{
    /// <summary>The reading's beat picked the 16th, and the transients' fold placed the marker within it.</summary>
    ReadingRefinedByFold,

    /// <summary>The reading gave no beat, so the fold alone chose the first beat.</summary>
    FoldAlone,
}

/// <summary>How a starter's marker was numbered within its bar.</summary>
public enum StarterBar
{
    /// <summary>Counted from the reading's downbeat, sure enough to use.</summary>
    Downbeat,

    /// <summary>Beat 0, because the downbeat's confidence was under the gate.</summary>
    UnsureDownbeat,

    /// <summary>Beat 0, because the reading's bars were not of four.</summary>
    NotInFours,

    /// <summary>Beat 0, because the reading had no downbeat.</summary>
    NoDownbeat,
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(Dictionary<string, WrittenWarp>))]
internal sealed partial class WrittenWarpsJsonContext : JsonSerializerContext { }
