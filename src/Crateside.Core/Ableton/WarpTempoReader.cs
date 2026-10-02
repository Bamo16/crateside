using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Ableton;

/// <summary>
/// A sample's warped tempo, read from its sidecar once and again only when the sidecar changes:
/// reading every sidecar in the library is about 400 MB (ADR 0038). Kept across restarts when given a path.
/// </summary>
/// <param name="isStarter">Whether a sample's sidecar is a starter warp crateside wrote, whose tempo is keytag's guess (#77).</param>
public sealed class WarpTempoReader(string? path = null, Func<string, bool>? isStarter = null)
{
    // Bump when WarpTempo.Of changes what it computes, or a kept tempo outlives the rule that made it.
    private const int Version = 2;

    private readonly string? _path = path;
    private readonly ConcurrentDictionary<string, TempoEntry> _read = new(
        Load(path),
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Lock _saving = new();
    private volatile bool _changed;

    /// <summary>Null when the sample has no sidecar, no saved default clip, only a starter warp, or cannot be read.</summary>
    public WarpTempo? Read(string audioPath)
    {
        if (
            new FileInfo($"{audioPath}.asd") is not { Exists: true } sidecar
            || isStarter?.Invoke(audioPath) is true
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

    private static int? SampleRateOf(string audioPath)
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
