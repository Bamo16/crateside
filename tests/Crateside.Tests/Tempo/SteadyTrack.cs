using AbleKit.Analysis;

namespace Crateside.Tests.Tempo;

/// <summary>A synthetic sidecar: 8th-note hits at one tempo, warped at another.</summary>
internal static class SteadyTrack
{
    internal const int SampleRate = 44_100;

    /// <summary>
    /// A track whose 8th notes sit at <paramref name="trackBpm"/> from 0.5 s, warped by one
    /// marker pair at <paramref name="warpBpm"/> on the first hit.
    /// </summary>
    internal static AnalysisFile Analysis(
        double warpBpm,
        double trackBpm,
        double stepAt = double.PositiveInfinity,
        double stepSeconds = 0,
        double seconds = 240,
        double hitsLate = 0
    )
    {
        var hits = Enumerable
            .Range(0, (int)(seconds * trackBpm / 30))
            .Select(i => 0.5 + hitsLate + i * 30 / trackBpm)
            .Select(time => time > stepAt ? time + stepSeconds : time)
            .Where(time => time < seconds)
            .ToArray();
        var frames = (int)(seconds * SampleRate);
        var beats = seconds * warpBpm / 60;

        return new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                [.. Enumerable.Repeat(0.5f, frames * 2)],
                2,
                [
                    .. hits.Select(
                        (time, i) => new Transient((int)(time * SampleRate), i % 2 is 0 ? 1f : 0.6f)
                    ),
                ],
                3
            ),
            Warp = new Warp
            {
                Markers =
                [
                    new(0.5, 0),
                    new(0.5 + 60 / warpBpm, 1),
                    new(0.5 + 61.875 / warpBpm, 1.03125),
                ],
            },
            Clip = new Clip
            {
                LoopStart = 0,
                LoopEnd = beats,
                HiddenLoopStart = 0,
                HiddenLoopEnd = beats,
                OutMarker = beats,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        };
    }
}
