using System.Diagnostics;
using AbleKit.Analysis;
using Crateside.Core.Ableton;

namespace Crateside.Core.Tempo;

/// <summary>Whether a steady warp should be retyped to a tempo its transients hold, and which (#99).</summary>
public static class TrueUpDecision
{
    /// <summary>The least a whole number must save over the track, in ms, to be worth typing.</summary>
    public const double WholeFloorMs = 2;

    /// <summary>The least a drift must accumulate over the track, in ms, to be corrected.</summary>
    public const double DriftFloorMs = 5;

    /// <summary>How many standard errors a drift must stand from nothing to be believed.</summary>
    public const double DriftSignificance = 3;

    /// <summary>How far an audible point may move beyond the drift a retype removes and its rephase, in ms.</summary>
    public const double MovesWithinMs = 5;

    private const int MinWindows = 6;
    private const double MinNoiseMsPerMinute = 0.1;

    /// <summary>How far a segment outside the clip may run from the clip's own tempo and still be retyped with it, in BPM.</summary>
    private const double SameTempo = 0.05;

    public static TrueUpVerdict Of(AnalysisFile analysis, int sampleRate)
    {
        if (SteadyBpm(analysis, sampleRate) is not { } bpm)
            return Leave(TrueUpReason.NotSteady);

        var all = analysis
            .Audio.Transients.Select(transient => (double)transient.Position / sampleRate)
            .ToArray();
        var strongest = GridCheck.Strongest(analysis.Audio.Transients, sampleRate);
        var seconds = analysis.Audio.HeadTable is [.., var frames]
            ? (double)frames / sampleRate
            : all.LastOrDefault();
        var before = GridCheck.Against(strongest, analysis.Warp);

        if (before.Windows.Count < MinWindows)
            return Leave(TrueUpReason.TooFewTransients);

        if (before.Steps.Count > 0)
            return Leave(TrueUpReason.Steps);

        var whole = Math.Round(bpm);

        if (
            whole > 0
            && Math.Abs(bpm - whole) > 0
            && seconds * 1000 * Math.Abs(bpm - whole) / bpm >= WholeFloorMs
            && GridCheck.Fit(all, whole) >= GridCheck.Fit(all, bpm)
            && Holds(strongest, analysis, whole, before, seconds) is { } byWhole
        )
            return Retype(bpm, whole, true, seconds, before, byWhole);

        if (
            Math.Abs(before.DriftMsPerMinute) * seconds / 60 < DriftFloorMs
            || Math.Abs(before.DriftMsPerMinute) < DriftSignificance * Noise(before)
        )
            return Leave(TrueUpReason.WithinNoise);

        var corrected = bpm / (1 + before.DriftMsPerMinute / 60_000);

        return Holds(strongest, analysis, corrected, before, seconds) is { } byDrift
            ? Retype(bpm, corrected, false, seconds, before, byDrift)
            : Leave(TrueUpReason.DoesNotHold);
    }

    /// <summary>How the warp's grid sits against the audio, for a steady warp to be shown as it is (ADR 0050).</summary>
    public static WarpGrid GridOf(AnalysisFile analysis, int sampleRate) =>
        Of(analysis, sampleRate) switch
        {
            TrueUpVerdict.Retype retype => new(null, retype.Bpm, retype.DriftMs),
            TrueUpVerdict.Leave(var reason) => new(reason, null, 0),
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// The warp with its steady stretch laid on the line through the stretch's first marker at this tempo,
    /// each marker keeping its beat, then later by <paramref name="shiftSeconds"/>; the rest stays (ADR 0050).
    /// </summary>
    public static Warp Retyped(AnalysisFile analysis, double bpm, double shiftSeconds = 0)
    {
        if (SteadyStretch(analysis.Warp, analysis.DefaultClip) is not var (first, last))
            return analysis.Warp;

        var anchor = analysis.Warp.Markers[first];

        return analysis.Warp with
        {
            Markers =
            [
                .. analysis.Warp.Markers.Select(
                    (marker, index) =>
                        index < first || index > last
                            ? marker
                            : marker with
                            {
                                Seconds =
                                    anchor.Seconds
                                    + (marker.Beat - anchor.Beat) * 60 / bpm
                                    + shiftSeconds,
                            }
                ),
            ],
        };
    }

    /// <summary>
    /// The first and last marker of the segments under the default clip, widened over each segment beside
    /// them within 0.05 BPM of the clip's end tempo; null with under two markers.
    /// </summary>
    public static (int First, int Last)? SteadyStretch(Warp warp, DefaultClip? clip)
    {
        var markers = warp.Markers;

        if (markers.Count < 2)
            return null;

        if (clip is null)
            return (0, markers.Count - 1);

        var first = Enumerable
            .Range(0, markers.Count - 1)
            .LastOrDefault(i => markers[i].Beat <= clip.Start);
        var last = Enumerable
            .Range(first, markers.Count - 1 - first)
            .Where(i => markers[i + 1].Beat >= clip.End)
            .DefaultIfEmpty(markers.Count - 2)
            .First();
        var (opening, closing) = (TempoOf(markers, first), TempoOf(markers, last));

        while (first > 0 && Agrees(TempoOf(markers, first - 1), opening))
            first--;

        while (last < markers.Count - 2 && Agrees(TempoOf(markers, last + 1), closing))
            last++;

        return (first, last + 1);
    }

    /// <summary>
    /// Whether nothing audible (the clip's ends, its loop, the markers inside it) sits further in the audio
    /// from where it was, at the same beat, than the drift removed, the rephase and 5 ms.
    /// </summary>
    public static bool Keeps(
        AnalysisFile analysis,
        Warp retyped,
        double driftMs,
        double shiftSeconds
    ) =>
        MovedMs(analysis, retyped)
        <= Math.Abs(driftMs) + Math.Abs(shiftSeconds) * 1000 + MovesWithinMs;

    /// <summary>The farthest anything audible moves in the audio between the warp and <paramref name="retyped"/>, in ms at the same beat.</summary>
    public static double MovedMs(AnalysisFile analysis, Warp retyped) =>
        AudibleBeats(analysis)
            .Select(beat =>
                analysis.Warp.SecondsAt(beat) is { } before && retyped.SecondsAt(beat) is { } after
                    ? Math.Abs(after - before) * 1000
                    : 0
            )
            .DefaultIfEmpty(0)
            .Max();

    private static IEnumerable<double> AudibleBeats(AnalysisFile analysis) =>
        analysis.DefaultClip is { Start: var start, End: var end }
            ?
            [
                start,
                end,
                analysis.Clip.LoopStart,
                analysis.Clip.LoopEnd,
                .. analysis
                    .Warp.Markers.Select(marker => marker.Beat)
                    .Where(beat => beat >= start && beat <= end),
            ]
            : analysis.Warp.Markers.Select(marker => marker.Beat);

    private static double? TempoOf(IReadOnlyList<WarpMarker> markers, int segment) =>
        markers[segment + 1].Seconds - markers[segment].Seconds is > 0 and var span
            ? (markers[segment + 1].Beat - markers[segment].Beat) / span * 60
            : null;

    private static bool Agrees(double? tempo, double? reference) =>
        tempo is { } bpm && reference is { } against && Math.Abs(bpm - against) <= SameTempo;

    /// <summary>
    /// The report at the new tempo when it leaves no drift worth believing, does not widen the spread,
    /// and moves nothing audible further than the drift it removes.
    /// </summary>
    private static GridReport? Holds(
        double[] times,
        AnalysisFile analysis,
        double bpm,
        GridReport before,
        double seconds
    ) =>
        Retyped(analysis, bpm) is var retyped
        && GridCheck.Against(times, retyped) is var after
        && after.Steps.Count is 0
        && Math.Abs(after.DriftMsPerMinute) <= Math.Abs(before.DriftMsPerMinute)
        && Math.Abs(after.DriftMsPerMinute) < DriftSignificance * Noise(after)
        && after.SpreadMs <= before.SpreadMs
        && Keeps(analysis, retyped, before.DriftMsPerMinute * seconds / 60, 0)
            ? after
            : null;

    /// <summary>The warp's tempo when its default clip holds one and the steady stretch under it runs to the last marker.</summary>
    private static double? SteadyBpm(AnalysisFile analysis, int sampleRate) =>
        analysis.DefaultClip is { } clip
        && WarpTempo.Of(clip, analysis.Warp, analysis.Audio.Overview, sampleRate)
            is { Shape: TempoShape.Steady, Bpm: var bpm }
        && SteadyStretch(analysis.Warp, clip) is (_, var last)
        && last == analysis.Warp.Markers.Count - 1
            ? bpm
            : null;

    /// <summary>The drift's standard error, never under a tenth of a ms a minute, which a grid this clean could not honestly claim.</summary>
    private static double Noise(GridReport report) =>
        Math.Max(report.DriftNoiseMsPerMinute, MinNoiseMsPerMinute);

    private static TrueUpVerdict.Retype Retype(
        double warpBpm,
        double bpm,
        bool whole,
        double seconds,
        GridReport before,
        GridReport after
    ) =>
        new(
            bpm,
            warpBpm,
            whole,
            before.DriftMsPerMinute * seconds / 60,
            after.DriftMsPerMinute * seconds / 60
        );

    private static TrueUpVerdict.Leave Leave(TrueUpReason reason) => new(reason);
}

public abstract record TrueUpVerdict
{
    private TrueUpVerdict() { }

    /// <param name="Whole">Whether <paramref name="Bpm"/> is the nearest whole number, rather than the tempo that removes the measured drift.</param>
    /// <param name="DriftMs">What the warp's drift adds up to over the track, in ms.</param>
    /// <param name="DriftAfterMs">The same at <paramref name="Bpm"/>.</param>
    public sealed record Retype(
        double Bpm,
        double WarpBpm,
        bool Whole,
        double DriftMs,
        double DriftAfterMs
    ) : TrueUpVerdict;

    public sealed record Leave(TrueUpReason Reason) : TrueUpVerdict;
}

public enum TrueUpReason
{
    /// <summary>No canonical stem of the song holds a warp the user saved.</summary>
    NoWarp,

    /// <summary>The warp changes tempo under its clip, has none to read, or changes again after its steady stretch.</summary>
    NotSteady,

    TooFewTransients,

    /// <summary>A step is #86's job, not a tempo's.</summary>
    Steps,

    /// <summary>It holds within what the windows can tell apart, or sits off a whole number the track is not at.</summary>
    WithinNoise,

    /// <summary>A tempo that removes the drift was found, but the grid does not hold at it.</summary>
    DoesNotHold,
}
