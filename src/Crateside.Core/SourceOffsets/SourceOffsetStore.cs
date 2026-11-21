using System.Text.Json.Serialization;

namespace Crateside.Core.SourceOffsets;

/// <summary>What each added source measured against its mix, kept across restarts in <c>source-offsets.json</c> (ADR 0111).</summary>
public sealed class SourceOffsetStore
{
    private readonly Lock _lock = new();
    private readonly string? _path;
    private readonly Dictionary<string, SourceOffsetRecord> _entries;

    public SourceOffsetStore(CratesidePaths paths)
        : this(paths.SourceOffsetsFile) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal SourceOffsetStore(string? path = null)
    {
        _path = path;
        _entries = new(
            path is not null
            && SourceOffsetsJsonContext.Default.DictionaryStringSourceOffsetRecord.ReadFileOrDefault(
                path
            )
                is { } entries
                ? entries
                : [],
            StringComparer.Ordinal
        );
    }

    /// <summary>The record of an added source by its upload, whether or not it still stands.</summary>
    public SourceOffsetRecord? Of(string sourceKey)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(sourceKey);
        }
    }

    /// <summary>Whether a standing measurement refuses the source: an edit, a drift, or not the same recording (ADR 0111).</summary>
    public bool IsRefused(string sourceKey) =>
        Of(sourceKey)
            is {
                IsUsable: true,
                Reading.Verdict: OffsetVerdict.Edit
                    or OffsetVerdict.Drift
                    or OffsetVerdict.NotTheSameRecording
            };

    public void Save(string sourceKey, SourceOffsetRecord record)
    {
        lock (_lock)
        {
            _entries[sourceKey] = record;

            if (_path is not null)
                SourceOffsetsJsonContext.Default.DictionaryStringSourceOffsetRecord.WriteFile(
                    _path,
                    _entries
                );
        }
    }
}

/// <summary>A file as measured, to tell whether it has changed since.</summary>
public sealed record FileStamp(string Path, long Length, DateTime Written)
{
    public static FileStamp? Of(string path) =>
        new FileInfo(path) is { Exists: true } file
            ? new FileStamp(path, file.Length, file.LastWriteTimeUtc)
            : null;

    public bool Holds => Of(Path) == this;

    /// <summary>Whether <paramref name="other"/> is this file wherever it now sits: same name, size and modified time.</summary>
    public bool IsSameFileAs(FileStamp other) =>
        Length == other.Length
        && Written == other.Written
        && string.Equals(
            System.IO.Path.GetFileName(Path),
            System.IO.Path.GetFileName(other.Path),
            StringComparison.OrdinalIgnoreCase
        );
}

/// <param name="Mix">The track's full mix, the reference.</param>
/// <param name="Source">The added source, as it was when measured.</param>
public sealed record SourceOffsetRecord(
    string TrackKey,
    FileStamp Mix,
    FileStamp Source,
    OffsetReading Reading,
    DateTimeOffset MeasuredAt
)
{
    /// <summary>Whether both files are as they were when measured.</summary>
    [JsonIgnore]
    public bool Stands => Mix.Holds && Source.Holds;

    /// <summary>Whether the offset can still be relied on: the source is as it was, and the mix is as it was or has gone from disk (ADR 0111, rule 27).</summary>
    [JsonIgnore]
    public bool IsUsable => IsUsableAt(Source.Path);

    /// <summary>Whether the offset can be relied on for the source now at <paramref name="sourcePath"/>, which may have moved since it was measured.</summary>
    public bool IsUsableAt(string sourcePath) =>
        FileStamp.Of(sourcePath) is { } now
        && Source.IsSameFileAs(now)
        && (Mix.Holds || !File.Exists(Mix.Path));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
)]
[JsonSerializable(typeof(Dictionary<string, SourceOffsetRecord>))]
internal sealed partial class SourceOffsetsJsonContext : JsonSerializerContext { }
