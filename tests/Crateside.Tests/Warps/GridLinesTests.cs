using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class GridLinesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public GridLinesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>120 BPM from beat 0 at 0 s, so a bar is 2 s and a beat half a second, over a 200 s file.</summary>
    private WarpGroup Warped()
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():n} (Instrumental - Full).flac");

        File.WriteAllBytes(path, [1, 2, 3]);
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                [.. Enumerable.Repeat(0.5f, 44_100 * 2)],
                2,
                [],
                3
            ) with
            {
                HeadTable = [200 * 44_100],
            },
            Warp = new Warp
            {
                Markers = [new(0, 0), new(120, 240), new(120.015625, 240.03125)],
                TimeSignature = new(4, 4),
            },
            Clip = new Clip
            {
                LoopStart = 4,
                LoopEnd = 8,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        }.Write($"{path}.asd");

        return WarpGroup.Of([
            new StemWarpReader(new WrittenWarps(), new TransientOrigins(), new LiveAnalyses()).Read(
                path
            ),
        ]);
    }

    [Fact]
    public void Eight_bars_in_the_view_draw_the_beats_with_the_bars_stronger()
    {
        var lines = GridLines.Between(Warped(), 0, 8, GridDensity.Medium);

        Assert.Equal(17, lines.Count);
        Assert.Equal((0.5, GridStrength.Beat), lines[1]);
        Assert.Equal((2, GridStrength.Bar), lines[4]);
        Assert.Equal(5, lines.Count(line => line.Strength is GridStrength.Bar));
    }

    [Fact]
    public void A_narrower_density_goes_down_to_sixteenths_in_a_closer_view()
    {
        var group = Warped();

        var medium = GridLines.Between(group, 0, 4, GridDensity.Medium);
        var narrow = GridLines.Between(group, 0, 4, GridDensity.Narrow);

        Assert.DoesNotContain(medium, line => line.At is 0.125);
        Assert.Contains(medium, line => line is (0.25, GridStrength.Subdivision));
        Assert.Contains(narrow, line => line is (0.125, GridStrength.Subdivision));
    }

    [Fact]
    public void A_wider_density_keeps_to_bars_while_beats_would_be_crowded()
    {
        var lines = GridLines.Between(Warped(), 0, 16, GridDensity.Wide);

        Assert.All(lines, line => Assert.Equal(GridStrength.Bar, line.Strength));
        Assert.Equal(9, lines.Count);
    }

    [Fact]
    public void The_whole_track_draws_every_eighth_bar_rather_than_a_wall()
    {
        var lines = GridLines.Between(Warped(), 0, 200, GridDensity.Medium);

        Assert.Equal(13, lines.Count);
        Assert.Equal(16, lines[1].At, 0.01);
    }

    [Fact]
    public void Off_and_a_track_without_a_warp_draw_nothing()
    {
        Assert.Empty(GridLines.Between(Warped(), 0, 16, GridDensity.Off));
        Assert.Empty(GridLines.Between(new WarpGroup([], null), 0, 16, GridDensity.Medium));
    }
}
