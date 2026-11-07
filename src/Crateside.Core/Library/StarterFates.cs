using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;

namespace Crateside.Core.Library;

/// <summary>How the starter warp crateside writes from beatdetect's reading compares with the warp the maintainer finished (ADR 0075).</summary>
public enum StarterFate
{
    /// <summary>The marker is on a beat of the finished warp, and its bar is the one the finished warp has.</summary>
    Kept,

    /// <summary>The finished warp's beats are not where the starter put its marker, or its tempo differs.</summary>
    BeatMoved,

    /// <summary>The starter's tempo is half or double the finished warp's, so the maintainer retyped it.</summary>
    OtherOctave,

    /// <summary>The downbeat was trusted and the finished warp numbers the bars differently.</summary>
    BarCorrected,

    /// <summary>The downbeat's confidence was under the gate, so the marker went on beat 0, and the finished warp numbers the bars differently.</summary>
    LeftAtBeatZero,

    /// <summary>The reading gives no starter tempo (it drifts, or it read none), so crateside writes no starter.</summary>
    NoStarter,
}

public static class StarterFates
{
    /// <summary>How far a beat may sit from a whole beat of the finished warp and still be on it.</summary>
    public const double BeatWithin = 0.2;

    /// <summary>The share by which the reading's tempo may differ from the finished warp's.</summary>
    public const double TempoWithin = TrackTempo.ReadingDisagreesAt;

    /// <summary>
    /// What today's starter would have been for this reading, set against the finished steady warp; null
    /// where the two cannot be compared (no reading, no steady warp, or a reading without a beat).
    /// </summary>
    public static StarterFate? Of(BeatReading? reading, WarpTempo? warp)
    {
        if (reading is null || warp is not { Shape: TempoShape.Steady, Line: { } line })
            return null;

        if (StarterGrid.Of(reading) is not { } grid)
            return StarterFate.NoStarter;

        if (grid.BeatSeconds is not { } beatSeconds)
            return null;

        var ratio = grid.Bpm / warp.Bpm;

        if (Math.Abs(ratio - 2) <= 2 * TempoWithin || Math.Abs(ratio - 0.5) <= 0.5 * TempoWithin)
            return StarterFate.OtherOctave;

        // The starter's marker is the first beat in the file, within the first beat length of its start.
        var beat = line.BeatAt(beatSeconds % (60 / grid.Bpm));

        if (Math.Abs(ratio - 1) > TempoWithin || Math.Abs(beat - Math.Round(beat)) > BeatWithin)
            return StarterFate.BeatMoved;

        return grid.DownbeatSeconds switch
        {
            { } downbeat when StartsBar(line.BeatAt(downbeat)) => StarterFate.Kept,
            { } => StarterFate.BarCorrected,
            _ when StartsBar(beat) => StarterFate.Kept,
            _ => StarterFate.LeftAtBeatZero,
        };
    }

    private static bool StartsBar(double beat) =>
        (long)Math.Round(beat) % StarterGrid.BeatsPerBar is 0;
}
