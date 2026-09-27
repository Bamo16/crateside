using System.Collections.Concurrent;

namespace Crateside.Core.Ableton;

/// <summary>
/// A sample's warped tempo, read from its sidecar once and again only when the sidecar changes:
/// reading every sidecar in the library is about 400 MB (ADR 0038).
/// </summary>
public sealed class WarpTempoReader
{
    private readonly ConcurrentDictionary<string, Entry> _read = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Null when the sample has no sidecar, no saved default clip, or cannot be read.</summary>
    public WarpTempo? Read(string audioPath)
    {
        if (new FileInfo($"{audioPath}.asd") is not { Exists: true } sidecar)
            return null;

        var stamp = (sidecar.Length, sidecar.LastWriteTimeUtc);

        if (_read.TryGetValue(sidecar.FullName, out var entry) && entry.Stamp == stamp)
            return entry.Tempo;

        var tempo =
            AbletonWarpReader.TryRead(sidecar.FullName, out var warp)
            && SampleRateOf(audioPath) is { } rate
                ? WarpTempo.Of(warp, rate)
                : null;

        _read[sidecar.FullName] = new Entry(stamp, tempo);

        return tempo;
    }

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

    private sealed record Entry((long Length, DateTime Written) Stamp, WarpTempo? Tempo);
}
