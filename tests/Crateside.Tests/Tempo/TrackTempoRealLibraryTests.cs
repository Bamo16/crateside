using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Tempo;

namespace Crateside.Tests.Tempo;

/// <summary>ADR 0049's consequences on the user's own warps and crateside's readings of them.</summary>
public sealed class TrackTempoRealLibraryTests
{
    private static readonly CratesidePaths Paths = new();
    private static readonly WarpTempoReader Warps = new();
    private static readonly BeatReader Readings = new(
        new ProcessRunner(),
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "beats.json")
    );

    [Theory]
    [InlineData("Lil Jon - Get Low")]
    [InlineData("Snail's House - Flash Drive")]
    [InlineData("Princess Superstar - Perfect")]
    public void A_warp_shows_as_itself_not_as_the_round_number_near_it(string name)
    {
        var tempo = Tempo(name);

        Assert.Equal(tempo.Warp!.Bpm, tempo.Value);
    }

    [Theory]
    [InlineData("Paul Oakenfold, Asher D - Ready Steady Go (Extended Mix)", "128.02")]
    [InlineData("50 Cent - In Da Club", "90.01")]
    public void A_real_tempo_off_the_grid_shows_its_hundredths(string name, string text) =>
        Assert.Equal(text, TempoFormat.Format(Tempo(name)).Text);

    private static TrackTempo Tempo(string name)
    {
        var audio = Path.Combine(Paths.LibraryFolder, $"{name} (Instrumental - Full).flac");
        Assert.SkipUnless(File.Exists(audio), $"{name} is not in the library on this machine.");

        var warp = Warps.Read(audio);
        var reading = Readings.Read(audio);
        Assert.SkipWhen(warp is null || reading is null, $"{name} has no warp or no kept reading.");

        return TrackTempo.Of(warp, reading, null)!;
    }
}
