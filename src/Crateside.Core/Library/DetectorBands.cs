namespace Crateside.Core.Library;

/// <summary>How close each detector's BPM comes to the warped one, song by song (handoff 08).</summary>
public sealed class DetectorBands
{
    private const double ExactWithin = 0.5;
    private const double CloseWithin = 2;

    private readonly Dictionary<Detector, IReadOnlyList<DetectorRead>> _reads;

    private DetectorBands(int songs, Dictionary<Detector, IReadOnlyList<DetectorRead>> reads)
    {
        Songs = songs;
        _reads = reads;
    }

    /// <summary>Songs with both a warp and a detector reading.</summary>
    public int Songs { get; }

    /// <summary>True once a detector reads fast (×4/3 or ×3/2), not only slow, so the band's name widens.</summary>
    public bool ReadsFastToo =>
        _reads
            .Values.SelectMany(reads => reads)
            .Any(read => read.Ratio > 1 && read.Band is TempoBand.TripletOrDotted);

    public static DetectorBands From(IEnumerable<LibrarySong> songs)
    {
        var compared = songs.Where(song => song.Tempo is not null).ToList();

        return new DetectorBands(
            compared.Count(song => song is { Run.Bpm: not null } or { Beats.Bpm: not null }),
            new()
            {
                [Detector.Keytag] = Reads(compared, song => song.Run?.Bpm),
                // crateside's own reading with the ffmpeg on PATH, not the ledger's (#76).
                [Detector.Beatdetect] = Reads(compared, song => song.Beats?.Bpm),
            }
        );
    }

    public IReadOnlyList<DetectorRead> Reads(Detector detector, TempoBand band) =>
        [
            .. _reads[detector]
                .Where(read => read.Band == band)
                .OrderBy(read => read.Ratio)
                .ThenBy(read => read.Song.Name, StringComparer.OrdinalIgnoreCase),
        ];

    /// <summary>Every read that is not exact, for charting where the misses land.</summary>
    public IReadOnlyList<DetectorRead> Misses(Detector detector) =>
        [.. _reads[detector].Where(read => read.Band is not TempoBand.Exact)];

    public int Largest =>
        _reads
            .Values.SelectMany(reads => reads.GroupBy(read => read.Band))
            .Select(band => band.Count())
            .DefaultIfEmpty()
            .Max();

    public static TempoBand BandOf(double warped, double read) =>
        Math.Abs(read - warped) switch
        {
            <= ExactWithin => TempoBand.Exact,
            <= CloseWithin => TempoBand.Close,
            _ when Near(read, warped, 0.5, 2) => TempoBand.HalfOrDouble,
            _ when Near(read, warped, 2.0 / 3, 3.0 / 4, 4.0 / 3, 3.0 / 2) =>
                TempoBand.TripletOrDotted,
            _ => TempoBand.Wrong,
        };

    private static bool Near(double read, double warped, params ReadOnlySpan<double> multiples)
    {
        foreach (var multiple in multiples)
        {
            if (Math.Abs(read - warped * multiple) <= CloseWithin)
                return true;
        }

        return false;
    }

    private static List<DetectorRead> Reads(
        List<LibrarySong> songs,
        Func<LibrarySong, double?> read
    ) =>
        [
            .. songs
                .Where(song => read(song) is > 0)
                .Select(song => new DetectorRead(song, song.Tempo!.Bpm, read(song)!.Value)),
        ];
}

public sealed record DetectorRead(LibrarySong Song, double Warped, double Read)
{
    public double Ratio => Read / Warped;

    public TempoBand Band => DetectorBands.BandOf(Warped, Read);
}

public enum Detector
{
    Keytag,
    Beatdetect,
}

public enum TempoBand
{
    Exact,
    Close,
    HalfOrDouble,
    TripletOrDotted,
    Wrong,
}
