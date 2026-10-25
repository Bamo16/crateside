using System.Security.Cryptography;
using System.Text;
using AbleKit.Analysis;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Samples;
using Crateside.Core.Staging;

namespace Crateside.Core.Warps;

/// <summary>A staged stem's clip colour: its kind picks the family, its song the shade (#130).</summary>
public static class ClipColours
{
    private static readonly ClipColor Drums = ClipColor.At(1, 2);

    private static readonly ClipColor[] Vocals =
    [
        ClipColor.At(1, 4),
        ClipColor.At(1, 5),
        ClipColor.At(1, 6),
        ClipColor.At(2, 4),
        ClipColor.At(2, 5),
        ClipColor.At(3, 4),
        ClipColor.At(3, 5),
        ClipColor.At(3, 6),
    ];

    private static readonly ClipColor[] Instrumentals =
    [
        ClipColor.At(1, 8),
        ClipColor.At(1, 9),
        ClipColor.At(1, 11),
        ClipColor.At(1, 12),
        ClipColor.At(2, 7),
        ClipColor.At(2, 8),
        ClipColor.At(2, 9),
        ClipColor.At(2, 11),
    ];

    /// <summary>Live's color for this stem, or null for one of no kind crateside colours.</summary>
    public static ClipColor? For(string audioPath)
    {
        var provenance = StemTagReader.Read(audioPath)?.Provenance;
        var preset = StemTagReader.PresetOf(audioPath);
        var song = provenance?.SourceVideoId ?? StemFileName.WithoutPreset(audioPath);

        if (StagedFile.IsWarpGuidePreset(preset))
            return Drums;

        return TaggingRunner.TypeFor(preset, provenance?.Type) switch
        {
            nameof(SampleType.Vocal) => Shade(Vocals, song),
            nameof(SampleType.Instrumental) => Shade(Instrumentals, song),
            _ => null,
        };
    }

    /// <summary>Stable across runs and machines, unlike <see cref="string.GetHashCode()"/>.</summary>
    private static ClipColor Shade(ClipColor[] family, string song) =>
        family[SHA256.HashData(Encoding.UTF8.GetBytes(song))[0] % family.Length];
}
