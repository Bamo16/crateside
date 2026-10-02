using Crateside.Core.Tempo;

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
                [Detector.Keytag] = Reads(
                    compared,
                    song => TrackTempo.Of(null, null, song.Run?.Bpm)
                ),
                // crateside's own reading with the ffmpeg on PATH, not the ledger's (#76).
                [Detector.Beatdetect] = Reads(
                    compared,
                    song => TrackTempo.Of(null, song.Beats, null)
                ),
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
        Func<LibrarySong, TrackTempo?> read
    ) =>
        [
            .. songs
                .Select(song => (Song: song, Warped: song.Bpm, Read: read(song)))
                .Where(pair => pair is { Warped: not null, Read: not null })
                .Select(pair => new DetectorRead(pair.Song, pair.Warped!, pair.Read!)),
        ];
}

/// <summary>One song's warped tempo beside a detector's; the band and ratio compare their raw figures.</summary>
public sealed record DetectorRead(LibrarySong Song, TrackTempo Warped, TrackTempo Read)
{
    public double Ratio => Read.Measured / Warped.Measured;

    public TempoBand Band => DetectorBands.BandOf(Warped.Measured, Read.Measured);
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
