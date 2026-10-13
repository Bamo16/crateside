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

    private const int MinWindows = 6;
    private const double MinNoiseMsPerMinute = 0.1;

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
            && Holds(strongest, analysis.Warp, whole, before) is { } byWhole
        )
            return Retype(bpm, whole, true, seconds, before, byWhole);

        if (
            Math.Abs(before.DriftMsPerMinute) * seconds / 60 < DriftFloorMs
            || Math.Abs(before.DriftMsPerMinute) < DriftSignificance * Noise(before)
        )
            return Leave(TrueUpReason.WithinNoise);

        var corrected = bpm / (1 + before.DriftMsPerMinute / 60_000);

        return Holds(strongest, analysis.Warp, corrected, before) is { } byDrift
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
    /// The warp with every marker after the first moved onto the line through it at this tempo, each
    /// keeping its beat, and all of them then moved later by <paramref name="shiftSeconds"/>.
    /// </summary>
    public static Warp Retyped(Warp warp, double bpm, double shiftSeconds = 0) =>
        warp.Markers is [var anchor, ..]
            ? warp with
            {
                Markers =
                [
                    .. warp.Markers.Select(marker =>
                        marker with
                        {
                            Seconds =
                                anchor.Seconds
                                + (marker.Beat - anchor.Beat) * 60 / bpm
                                + shiftSeconds,
                        }
                    ),
                ],
            }
            : warp;

    /// <summary>The report at the new tempo when it leaves no drift worth believing and does not widen the spread.</summary>
    private static GridReport? Holds(double[] times, Warp warp, double bpm, GridReport before) =>
        GridCheck.Against(times, Retyped(warp, bpm)) is var after
        && after.Steps.Count is 0
        && Math.Abs(after.DriftMsPerMinute) <= Math.Abs(before.DriftMsPerMinute)
        && Math.Abs(after.DriftMsPerMinute) < DriftSignificance * Noise(after)
        && after.SpreadMs <= before.SpreadMs
            ? after
            : null;

    private static double? SteadyBpm(AnalysisFile analysis, int sampleRate) =>
        analysis.DefaultClip is { } clip
        && WarpTempo.Of(clip, analysis.Warp, analysis.Audio.Overview, sampleRate)
            is { Shape: TempoShape.Steady, Bpm: var bpm }
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

    /// <summary>The warp changes tempo, or has none to read.</summary>
    NotSteady,

    TooFewTransients,

    /// <summary>A step is #86's job, not a tempo's.</summary>
    Steps,

    /// <summary>It holds within what the windows can tell apart, or sits off a whole number the track is not at.</summary>
    WithinNoise,

    /// <summary>A tempo that removes the drift was found, but the grid does not hold at it.</summary>
    DoesNotHold,
}
