using AbleKit.Analysis;

namespace Crateside.Tests.Tempo;

/// <summary>
/// The hand warp of Eminem – <i>Without Me</i> (<c>tqxRidAWER8</c>) as it was before a true-up on
/// 2026-10-19: a quiet intro at about 170, then 105 and 112.84 BPM, and 112.2503 from beat 6.
/// </summary>
internal static class WithoutMe
{
    internal const double ClipStart = 86.93120551323676;

    private const double TrackBpm = 112.25285;
    private const double Length = 292;

    internal static readonly WarpMarker[] Markers =
    [
        new(0.7204762248822177, -4),
        new(1.4243310620908196, -2),
        new(1.7777550866481273, -1),
        new(2.131269781157488, 0),
        new(2.83630387515338, 2),
        new(3.1890702422166917, 3),
        new(4.327120219550109, 5),
        new(4.85884350024975, 6),
        new(4.875547237736621, 6.03125),
    ];

    internal static Warp Warp => new() { Markers = Markers };

    /// <summary>Its markers and default clip, with 8th-note hits at 112.25285 from beat 6 and none in the intro.</summary>
    internal static AnalysisFile Analysis()
    {
        var hits = Enumerable
            .Range(0, (int)(Length * TrackBpm / 30))
            .Select(i => Markers[7].Seconds + i * 30 / TrackBpm)
            .Where(time => time < Length)
            .ToArray();

        return new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                [.. Enumerable.Repeat(0.5f, (int)(Length * SteadyTrack.SampleRate))],
                1,
                [
                    .. hits.Select(
                        (time, i) =>
                            new Transient(
                                (int)(time * SteadyTrack.SampleRate),
                                i % 2 is 0 ? 1f : 0.6f
                            )
                    ),
                ],
                3
            ),
            Warp = Warp,
            Clip = new Clip
            {
                LoopStart = ClipStart,
                LoopEnd = 542.0814885635198,
                HiddenLoopStart = 0,
                HiddenLoopEnd = 483.8820861430236,
                OutMarker = 483.8820861430236,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        };
    }
}
