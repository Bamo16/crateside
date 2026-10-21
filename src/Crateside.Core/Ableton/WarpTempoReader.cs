using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Crateside.Core.Warps;

namespace Crateside.Core.Ableton;

/// <summary>
/// A sample's warped tempo, read from its sidecar once and again only when the sidecar changes:
/// reading every sidecar in the library is about 400 MB (ADR 0038). Kept across restarts when given a path.
/// </summary>
public sealed class WarpTempoReader
{
    // Bump when WarpTempo.Of changes what it computes, or a kept tempo outlives the rule that made it.
    private const int Version = 5;

    private readonly string? _path;
    private readonly WrittenWarps? _written;
    private readonly ConcurrentDictionary<string, TempoEntry> _read;
    private readonly Lock _saving = new();
    private volatile bool _changed;

    public WarpTempoReader(CratesidePaths paths, WrittenWarps written)
        : this(paths.TempoCache, written) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal WarpTempoReader(string? path = null, WrittenWarps? written = null)
    {
        _path = path;
        _written = written;
        _read = new(Load(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Null when the sample has no sidecar, no saved default clip, only a starter warp, or cannot be read.</summary>
    public WarpTempo? Read(string audioPath)
    {
        if (
            new FileInfo($"{audioPath}.asd") is not { Exists: true } sidecar
            // A starter warp is crateside's own reading, not the user's (#77).
            || _written?.IsStarter(audioPath) is true
        )
            return null;

        var stamp = new TempoStamp(sidecar.Length, sidecar.LastWriteTimeUtc);

        if (_read.TryGetValue(sidecar.FullName, out var entry) && entry.Stamp == stamp)
            return entry.Tempo;

        var tempo =
            AbletonReads.TryReadAnalysis(sidecar.FullName, out var analysis)
            && SampleRateOf(audioPath) is { } rate
                ? WarpTempo.Of(analysis, rate)
                : null;

        _read[sidecar.FullName] = new TempoEntry(stamp, tempo);
        _changed = true;

        return tempo;
    }

    /// <summary>Writes what was read since the last save, leaving out sidecars that are gone.</summary>
    public void Save()
    {
        if (_path is null || !_changed)
            return;

        lock (_saving)
        {
            _changed = false;

            var kept = new TempoFile(
                Version,
                _read
                    .Where(pair => File.Exists(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            );

            try
            {
                WarpTempoJsonContext.Default.TempoFile.WriteFile(_path, kept);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _changed = true;
            }
        }
    }

    private static Dictionary<string, TempoEntry> Load(string? path) =>
        path is not null
        && WarpTempoJsonContext.Default.TempoFile.ReadFileOrDefault(path)
            is { Version: Version, Entries: { } entries }
            ? entries
            : [];

    internal static int? SampleRateOf(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);

            return file is { Properties.AudioSampleRate: > 0 and var rate } ? rate : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed record TempoStamp(long Length, DateTime Written);

internal sealed record TempoEntry(TempoStamp Stamp, WarpTempo? Tempo);

internal sealed record TempoFile(int Version, Dictionary<string, TempoEntry> Entries);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(TempoFile))]
internal sealed partial class WarpTempoJsonContext : JsonSerializerContext { }
