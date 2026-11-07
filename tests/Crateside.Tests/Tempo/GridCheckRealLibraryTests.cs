using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

/// <summary>The grid check on Get Low, whose last minute sits 50 ms late (docs/notes/2026-10-02-the-six-warps-in-91.md).</summary>
public sealed class GridCheckRealLibraryTests
{
    private static readonly string Drums = Environment.SpecialFolder.UserProfile.GetFolderPath(
        "Music",
        "Stems",
        "Drums",
        "Lil Jon - Get Low (Drums).flac.asd"
    );

    [Fact]
    public void Get_Low_steps_about_50_ms_at_4_40_against_a_steady_101()
    {
        var report = GridCheck.Against(Times(), 101);

        var step = Assert.Single(report.Steps);
        Assert.InRange(step.Ms, 45, 55);
        Assert.InRange(step.At, 270, 290);
    }

    [Fact]
    public void Get_Low_reads_101_before_its_step() =>
        Assert.Equal(101, GridCheck.Refine([.. Times().Where(time => time < 279)], 101), 2);

    [Fact]
    public void The_warp_with_markers_around_the_step_holds_within_5_ms()
    {
        var analysis = Read();
        Assert.SkipWhen(
            analysis.Warp.Markers.Count < 4,
            "Get Low's warp has no markers around its step."
        );

        var report = GridCheck.Against(Times(), analysis.Warp);

        Assert.Empty(report.Steps);
        Assert.InRange(report.SpreadMs, 0, 10);
    }

    private static double[] Times() => GridCheck.Strongest(Read().Audio.Transients, 44100);

    private static AnalysisFile Read()
    {
        Assert.SkipUnless(File.Exists(Drums), "Get Low's drum stem is not kept on this machine.");

        return AnalysisFile.Read(Drums);
    }
}
