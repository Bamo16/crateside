namespace Crateside.Core.Ableton;

/// <summary>The Tempo group crateside derives from a file's warp: the two bands of one octave nearest its tempo (ADR 0113).</summary>
public static class TempoTags
{
    public const string Group = "Tempo";

    /// <summary>How many bands one octave is cut into; changing it renames every tag on the next backing pass.</summary>
    public const int BandsPerOctave = 12;

    private const double OctaveFloor = 80;

    private static readonly string[] Names =
    [
        .. Enumerable.Range(0, BandsPerOctave).Select(NameOf),
    ];

    public static bool IsKnown(string value) => Names.Contains(value, StringComparer.Ordinal);

    public static bool IsKeyword(string keyword) =>
        keyword.StartsWith($"{Group}|", StringComparison.Ordinal);

    /// <summary>The two tags whose centres sit nearest <paramref name="bpm"/> once folded into 80–160, in band order; none for a tempo that is no number above zero.</summary>
    public static IReadOnlyList<string> For(double bpm)
    {
        if (!double.IsFinite(bpm) || bpm <= 0)
            return [];

        // Bands from the octave's floor, wrapping; a centre sits half a band into its own.
        var octaves = Math.Log2(bpm / OctaveFloor);
        var position = BandsPerOctave * (octaves - Math.Floor(octaves));
        var lower = (int)Math.Floor(position - 0.5);

        return
        [
            .. new[] { lower, lower + 1 }
                .Select(band => (band + BandsPerOctave) % BandsPerOctave)
                .Order()
                .Select(band => $"{Group}|{Names[band]}"),
        ];
    }

    /// <summary>The bag with its Tempo tags replaced by the ones the warp's tempo calls for, and none for a file with no warp; a bag with no whole key is left as it is.</summary>
    public static IReadOnlyList<string> Reconcile(
        IReadOnlyList<string> keywords,
        WarpTempo? tempo
    ) =>
        FileTagsReading.ReadKey(keywords).Key is null
            ? keywords
            :
            [
                .. keywords.Where(keyword => !IsKeyword(keyword)),
                .. tempo is null ? [] : For(tempo.Bpm),
            ];

    private static string NameOf(int band)
    {
        var centre = OctaveFloor * Math.Pow(2, (band + 0.5) / BandsPerOctave);

        return $"≈{Math.Round(centre):0} · {Math.Round(2 * centre):0}";
    }
}
