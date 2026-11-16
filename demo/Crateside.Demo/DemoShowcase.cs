using AbleKit.Analysis;
using Crateside.Core.Ledger;

namespace Crateside.Demo;

/// <summary>The one track that has what the warp view draws and no real song in the demo does: judged sections in other keys, windows, a drifting tempo, and 7/4.</summary>
internal static class DemoShowcase
{
    public const string VideoId = "DemoTrack03";

    public static TimeSignature Signature { get; } = new(7, 4);

    /// <summary>A user's window in the primary key, a user's window in another, and the judge's window in a third.</summary>
    public static IReadOnlyList<RunWindow> Windows { get; } =
    [
        new(0, 36, "Verse", WindowAsker.User),
        new(120, 170, "Bridge", WindowAsker.User),
        new(250, 290, "Breakdown", WindowAsker.Judge),
    ];

    /// <summary>The seconds of every beat from beat 0 to past the track's end, the tempo swinging 6% about the song's own.</summary>
    public static IReadOnlyList<double> BeatSeconds(DemoTrack track)
    {
        List<double> seconds = [DemoAudio.Lead];

        while (seconds[^1] < track.Seconds + 5)
        {
            var bpm = track.Bpm * (1 + 0.06 * Math.Sin(2 * Math.PI * (seconds.Count - 1) / 150));

            seconds.Add(seconds[^1] + 60 / bpm);
        }

        return seconds;
    }

    /// <summary>A marker on each bar line, as Live writes a hand-warped song.</summary>
    public static IReadOnlyList<WarpMarker> Markers(DemoTrack track)
    {
        var beats = BeatSeconds(track);

        return
        [
            .. Enumerable
                .Range(0, (beats.Count - 1) / Signature.Numerator + 1)
                .Select(bar => new WarpMarker(
                    beats[bar * Signature.Numerator],
                    bar * Signature.Numerator
                )),
        ];
    }

    /// <summary>The loop Live has switched on, bars 3 to 13, under the Verse and Bridge tags.</summary>
    public const double LoopStartBeats = 14;

    public const double LoopEndBeats = 84;

    /// <summary>The whole bars the track holds, in beats.</summary>
    public static double LoopBeats(DemoTrack track) =>
        (BeatSeconds(track).Count(beat => beat <= track.Seconds) - 1)
        / Signature.Numerator
        * Signature.Numerator;
}
