using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class StarterGridTests : IDisposable
{
    private const double BeatSeconds = 0.5;
    private const double BarSeconds = BeatSeconds * StarterGrid.BeatsPerBar;

    /// <summary>Where the synthesized tracks' first bar starts, so a downbeat read at the top of the file is wrong.</summary>
    private const double FirstBarSeconds = 1;

    private readonly string _folder = Directory.CreateTempSubdirectory("crateside-grid").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_sure_downbeat_numbers_the_starters_beats() =>
        Assert.Equal(new StarterGrid(120, 1.5), StarterGrid.Of(Reading(confidence: 0.6)));

    [Fact]
    public void An_unsure_downbeat_leaves_which_beat_starts_a_bar_a_guess() =>
        Assert.Equal(new StarterGrid(120, null), StarterGrid.Of(Reading(confidence: 0.599)));

    [Fact]
    public void A_downbeat_in_bars_of_three_is_not_used_on_a_grid_of_four() =>
        Assert.Equal(
            new StarterGrid(120, null),
            StarterGrid.Of(Reading(confidence: 0.2) with { BeatsPerBar = 3 })
        );

    [Fact]
    public void A_reading_without_a_tempo_gives_no_grid() =>
        Assert.Null(StarterGrid.Of(new BeatReading { BeatOffsetMs = 500 }));

    [Fact]
    public void A_drifting_reading_gives_no_grid() =>
        Assert.Null(
            StarterGrid.Of(Reading(confidence: 0.2) with { Windows = [118, 119, 120, 121, 122] })
        );

    [Fact]
    public async Task A_track_with_a_clear_downbeat_puts_it_on_a_bar_line()
    {
        var grid = StarterGrid.Of(await ReadAsync("accented.wav", Accented()));

        Assert.NotNull(grid?.DownbeatSeconds);

        var fromBarLine = Mod(grid.DownbeatSeconds.Value - FirstBarSeconds, BarSeconds);

        Assert.InRange(Math.Min(fromBarLine, BarSeconds - fromBarLine), 0, 0.025);
    }

    [Fact]
    public async Task A_track_whose_beats_are_all_alike_gives_no_downbeat()
    {
        var grid = StarterGrid.Of(await ReadAsync("flat.wav", seconds => Kick(1, seconds)));

        Assert.NotNull(grid);
        Assert.Null(grid.DownbeatSeconds);
    }

    private static BeatReading Reading(double confidence) =>
        new()
        {
            Bpm = 120,
            BeatOffsetMs = 500,
            DownbeatOffsetMs = 1500,
            DownbeatConfidence = confidence,
            BeatsPerBar = 4,
        };

    private async Task<BeatReading> ReadAsync(string name, Func<double, double> sampleAt)
    {
        var pinned = RealPaths.PinnedBuild(FfmpegFilter.Beatdetect);
        var audio = Path.Combine(_folder, name);
        TestWav.Write(audio, 60, sampleAt);

        return await new BeatReader(new ProcessRunner(), fileName: pinned.Path).MeasureAsync(
            audio,
            TestContext.Current.CancellationToken
        );
    }

    /// <summary>120 BPM: a kick on every beat, loudest on the bar's first, a hat between, and a bass note that changes with each bar.</summary>
    private static Func<double, double> Accented()
    {
        var noise = new Random(1);
        double[] bass = [110, 146.83, 164.81, 123.47];

        return seconds =>
        {
            var inBar = Mod(seconds - FirstBarSeconds, BarSeconds);
            var bar = (int)Math.Floor((seconds - FirstBarSeconds) / BarSeconds);

            return Kick(inBar < BeatSeconds ? 1 : 0.35, seconds)
                + 0.3
                    * (noise.NextDouble() * 2 - 1)
                    * Math.Exp(-60 * Mod(seconds - BeatSeconds / 2, 1))
                + 0.2 * Math.Sin(2 * Math.PI * bass[Mod(bar, bass.Length)] * seconds);
        };
    }

    private static double Kick(double level, double seconds) =>
        0.8
        * level
        * Math.Sin(2 * Math.PI * 55 * seconds)
        * Math.Exp(-25 * Mod(seconds, BeatSeconds));

    private static double Mod(double value, double by) => (value % by + by) % by;

    private static int Mod(int value, int by) => (value % by + by) % by;
}
