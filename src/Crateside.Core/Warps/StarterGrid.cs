using Crateside.Core.Beats;
using Crateside.Core.Tempo;

namespace Crateside.Core.Warps;

/// <summary>What a starter warp takes from the reference stem's reading: its tempo, a beat, and the downbeat when the filter is sure of it (#134, ADR 0054).</summary>
/// <param name="BeatSeconds">A beat in the file, which picks the 16th the marker goes on; null from a build without it.</param>
/// <param name="DownbeatSeconds">A downbeat in the file, or null when which beat starts a bar is a guess.</param>
public sealed record StarterGrid(double Bpm, double? BeatSeconds, double? DownbeatSeconds)
{
    /// <summary>The downbeat confidence from which the filter's downbeat is trusted.</summary>
    public const double DownbeatSureAt = 0.6;

    public const int BeatsPerBar = 4;

    /// <summary>Null when the reading gives no starter tempo (<see cref="TrackTempo.StarterBpm"/>).</summary>
    public static StarterGrid? Of(BeatReading? reading) =>
        TrackTempo.StarterBpm(reading) is { } bpm
            ? new(
                bpm,
                reading?.BeatOffsetMs / 1000,
                reading
                    is {
                        DownbeatConfidence: >= DownbeatSureAt,
                        DownbeatOffsetMs: { } downbeat,
                        BeatsPerBar: BeatsPerBar,
                    }
                    ? downbeat / 1000
                    : null
            )
            : null;
}
