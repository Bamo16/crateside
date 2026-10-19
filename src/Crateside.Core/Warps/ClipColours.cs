using System.Security.Cryptography;
using System.Text;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Samples;
using Crateside.Core.Staging;

namespace Crateside.Core.Warps;

/// <summary>A staged stem's clip colour: its kind picks the family, its song the shade (#130).</summary>
public static class ClipColours
{
    private const int CellsPerRow = 14;

    private static readonly int Drums = Cell(1, 2);

    private static readonly int[] Vocals =
    [
        Cell(1, 4),
        Cell(1, 5),
        Cell(1, 6),
        Cell(2, 4),
        Cell(2, 5),
        Cell(3, 4),
        Cell(3, 5),
        Cell(3, 6),
    ];

    private static readonly int[] Instrumentals =
    [
        Cell(1, 8),
        Cell(1, 9),
        Cell(1, 11),
        Cell(1, 12),
        Cell(2, 7),
        Cell(2, 8),
        Cell(2, 9),
        Cell(2, 11),
    ];

    /// <summary>Live's <c>ColorIndex</c> for this stem, or null for one of no kind crateside colours.</summary>
    public static int? For(string audioPath)
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
    private static int Shade(int[] family, string song) =>
        family[SHA256.HashData(Encoding.UTF8.GetBytes(song))[0] % family.Length];

    /// <summary>Row and column from 1; Live numbers its palette row by row from 0 at the top left.</summary>
    private static int Cell(int row, int column) => (row - 1) * CellsPerRow + column - 1;
}
