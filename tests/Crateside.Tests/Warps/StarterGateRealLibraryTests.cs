using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Samples;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

/// <summary>Why a reading beatdetect calls moving still gets a starter: one tempo holds on most of them (#135).</summary>
public sealed class StarterGateRealLibraryTests
{
    private const double WandersAt = 0.7;
    private const int MinToJudge = 20;

    private static readonly CratesidePaths Paths = new();
    private static readonly BeatReader Readings = new(
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "beats.json")
    );

    [Fact]
    public void Most_steady_tracks_beatdetect_calls_moving_hold_one_tempo_within_10_ms()
    {
        Assert.SkipUnless(Directory.Exists(Paths.LibraryFolder), "The library is not here.");

        var written = new WrittenWarps(Paths);
        var warps = new WarpTempoReader();
        bool[] holds =
        [
            .. Directory
                .EnumerateFiles(Paths.LibraryFolder, "*.flac", SearchOption.AllDirectories)
                .Where(path =>
                    Readings.Read(path)
                        is { Bpm: not null, TempoWander: { } wander, Segments: var segments }
                    && (wander >= WandersAt || segments.Count > 1)
                    && !written.IsCratesides(path)
                    && warps.Read(path) is { Shape: TempoShape.Steady }
                )
                .Select(Holds)
                .OfType<bool>(),
        ];

        Assert.SkipWhen(
            holds.Length < MinToJudge,
            $"Only {holds.Length} steady tracks have a reading from a build that reports wander."
        );

        Assert.True(
            holds.Count(held => held) * 2 >= holds.Length,
            $"{holds.Count(held => held)} of {holds.Length} hold"
        );
    }

    private static bool? Holds(string path)
    {
        if (
            !AnalysisFile.TryRead(StemFileName.AnalysisFor(path), out var analysis)
            || WarpTempoReader.SampleRateOf(path) is not { } rate
            || Readings.Read(path) is not { Bpm: { } bpm }
        )
            return null;

        var transients = analysis.Audio.Transients;

        return SidecarWriter.Holds(
            GridCheck.Against(
                GridCheck.Strongest(transients, rate),
                SidecarWriter.Refined(bpm, transients, rate)
            )
        );
    }
}
