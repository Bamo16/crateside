using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

/// <summary>The retype decision against the warps in this machine's library (docs/notes/2026-10-13-retyping-warps-in-the-library.md).</summary>
public sealed class TrueUpRealLibraryTests
{
    private static readonly CratesidePaths Paths = new();

    [Fact]
    public void A_warp_at_a_stable_tempo_between_whole_numbers_is_left()
    {
        var verdict = Decide("Snoop Dogg - Gin and Juice (Instrumental - Full).flac");

        Assert.Equal(new TrueUpVerdict.Leave(TrueUpReason.WithinNoise), verdict);
    }

    [Fact]
    public void A_warp_that_drifts_at_a_tempo_that_is_not_whole_is_retyped_and_holds_on_a_copy()
    {
        var name = "Rob Zombie - Dragula (Instrumental - Full).flac";
        var original = Read(name);
        var retype = Assert.IsType<TrueUpVerdict.Retype>(Decide(name));
        var copy = Path.Combine(Path.GetTempPath(), "crateside-tests", $"{Guid.NewGuid():n}.asd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            (original with { Warp = TrueUpDecision.Retyped(original.Warp, retype.Bpm) }).Write(
                copy
            );
            var written = AnalysisFile.Read(copy);
            var report = GridCheck.Against(
                GridCheck.Strongest(written.Audio.Transients, 44100),
                written.Warp
            );

            Assert.False(retype.Whole);
            Assert.Equal(original.Audio.Transients, written.Audio.Transients);
            Assert.Equal(original.Clip, written.Clip);
            Assert.Equal(original.DefaultClip, written.DefaultClip);
            Assert.InRange(report.DriftMsPerMinute, -0.5, 0.5);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static TrueUpVerdict Decide(string name) => TrueUpDecision.Of(Read(name), 44100);

    private static AnalysisFile Read(string name)
    {
        var path = Path.Combine(Paths.LibraryFolder, $"{name}.asd");
        Assert.SkipUnless(File.Exists(path), $"{name} is not in the library on this machine.");

        return AnalysisFile.Read(path);
    }
}
