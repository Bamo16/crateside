using Crateside.Core.Beats;

namespace Crateside.Core.Warps;

/// <summary>Where a starter warp's one marker goes, read off the filter's beat grid (#101).</summary>
/// <param name="MarkerSeconds">The first downbeat when the filter is sure of one, else the first beat.</param>
/// <param name="DownbeatKnown">False when the marker is only a beat, and which beat is beat 1 is a guess.</param>
public sealed record StarterGrid(double Bpm, double MarkerSeconds, bool DownbeatKnown)
{
    /// <summary>The downbeat confidence from which the filter's downbeat is trusted.</summary>
    public const double DownbeatSureAt = 0.03;

    public static StarterGrid? Of(BeatReading reading) =>
        reading switch
        {
            {
                Bpm: { } bpm,
                DownbeatConfidence: >= DownbeatSureAt,
                DownbeatOffsetMs: { } downbeat
            } => new(bpm, downbeat / 1000, true),
            { Bpm: { } bpm, BeatOffsetMs: { } beat } => new(bpm, beat / 1000, false),
            _ => null,
        };
}
