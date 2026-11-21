using Crateside.Core.Ableton;
using Crateside.Core.Tempo;

namespace Crateside.Core.Library;

/// <summary>The library by headline BPM in bins of ten, ordered by tempo (handoff 08, ADR 0049).</summary>
public sealed record TempoCounts(
    IReadOnlyList<TempoBin> Bins,
    int Warped,
    int Steady,
    int MostlySteady,
    int Drifting
)
{
    /// <summary>How many songs must share a tempo for it to be called out in its bin.</summary>
    public const int SharedBy = 10;

    private const int BinWidth = 10;
    private const int Floor = 70;
    private const int SparseBelow = 3;

    public int Largest => Bins.Count is 0 ? 0 : Bins.Max(bin => bin.Songs);

    public static TempoCounts From(IEnumerable<LibrarySong> songs)
    {
        var listed = songs.ToList();
        var tempos = listed.Select(song => song.Tempo).OfType<WarpTempo>().ToList();
        var bpms = listed
            .Select(song => song.Bpm)
            .OfType<TrackTempo>()
            .Select(tempo => Whole(tempo.Value))
            .ToList();

        return new TempoCounts(
            Binned(bpms),
            tempos.Count,
            tempos.Count(tempo => tempo.Shape is TempoShape.Steady),
            tempos.Count(tempo => tempo.Shape is TempoShape.MostlySteady),
            tempos.Count(tempo => tempo.Shape is TempoShape.Drifting)
        );
    }

    public static int Whole(double bpm) => (int)Math.Round(bpm, MidpointRounding.AwayFromZero);

    private static List<TempoBin> Binned(List<int> bpms)
    {
        if (bpms.Count is 0)
            return [];

        var highest = bpms.Max() / BinWidth * BinWidth;

        List<(int Low, int High)> bins =
        [
            .. Enumerable
                .Range(0, Math.Max(0, (highest - Floor) / BinWidth + 1))
                .Select(i => Floor + i * BinWidth)
                .Select(low => (low, low + BinWidth - 1)),
        ];

        // Everything below the floor is one bin, and so is a sparse run at the top.
        if (bpms.Min() is var slowest && slowest < Floor)
            bins.Insert(0, (slowest / BinWidth * BinWidth, Floor - 1));

        var top = bins.Count;

        while (top > 1 && Count(bpms, bins[top - 1]) < SparseBelow)
            top--;

        if (top < bins.Count - 1)
            bins = [.. bins.Take(top), (bins[top].Low, bins[^1].High)];

        return [.. bins.Select(bin => Bin(bpms, bin.Low, bin.High))];
    }

    private static int Count(List<int> bpms, (int Low, int High) bin) =>
        bpms.Count(bpm => bpm >= bin.Low && bpm <= bin.High);

    private static TempoBin Bin(List<int> bpms, int low, int high)
    {
        var inside = bpms.Where(bpm => bpm >= low && bpm <= high).ToList();

        return new TempoBin(
            low,
            high,
            inside.Count,
            [
                .. inside
                    .GroupBy(bpm => bpm)
                    .Where(group => group.Count() >= SharedBy)
                    .Select(group => new TempoPeak(group.Key, group.Count()))
                    .OrderByDescending(peak => peak.Songs)
                    .ThenBy(peak => peak.Bpm),
            ]
        );
    }
}

public sealed record TempoBin(int Low, int High, int Songs, IReadOnlyList<TempoPeak> Peaks)
{
    public string Label => TempoFormat.Bin(Low, High);

    /// <summary>Songs on a tempo that <see cref="TempoCounts.SharedBy"/> or more share.</summary>
    public int PeakSongs => Peaks.Sum(peak => peak.Songs);
}

public sealed record TempoPeak(int Bpm, int Songs);
