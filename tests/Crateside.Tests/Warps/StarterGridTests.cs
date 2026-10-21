using System.Text.Json;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class StarterGridTests
{
    private const string Harness = @"V:\Repos\beatdetect-harness";
    private static readonly string Pinned = Path.Combine(
        Harness,
        "builds",
        "1.3.0-1e57fec",
        "ffmpeg.exe"
    );

    [Fact]
    public void A_sure_downbeat_numbers_the_starters_beats() =>
        Assert.Equal(new StarterGrid(120, 1.5), StarterGrid.Of(Reading(confidence: 0.03)));

    [Fact]
    public void An_unsure_downbeat_leaves_which_beat_starts_a_bar_a_guess() =>
        Assert.Equal(new StarterGrid(120, null), StarterGrid.Of(Reading(confidence: 0.029)));

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
    public async Task A_steady_corpus_tracks_downbeat_falls_on_a_bar_line_of_its_warp()
    {
        var truth = Corpus("maroon-5-sugar");
        var grid = StarterGrid.Of(await ReadAsync(truth));

        Assert.NotNull(grid?.DownbeatSeconds);
        Assert.InRange(truth.GridErrorMs(grid.DownbeatSeconds.Value), -25, 25);
        Assert.InRange(truth.BarOffsetFromClipStart(grid.DownbeatSeconds.Value), -0.1, 0.1);
    }

    [Fact]
    public async Task A_corpus_track_the_filter_is_unsure_of_gives_no_downbeat()
    {
        var grid = StarterGrid.Of(await ReadAsync(Corpus("adele-rolling-in-the-deep")));

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

    private static CorpusTrack Corpus(string id)
    {
        var truthFile = Path.Combine(Harness, "corpus", "truth.jsonl");

        if (!File.Exists(truthFile) || !File.Exists(Pinned))
            Assert.Skip("the beatdetect harness and its pinned build are not here");

        var row = File.ReadLines(truthFile)
            .Where(line => line.Length > 0)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .First(row => row.GetProperty("id").GetString() == id);
        var audio = Path.Combine(Harness, row.GetProperty("audio").GetString()!);

        if (!File.Exists(audio))
            Assert.Skip($"{id}'s audio is not in the harness");

        return new CorpusTrack(audio, row);
    }

    private static Task<BeatReading> ReadAsync(CorpusTrack track) =>
        new BeatReader(new ProcessRunner(), fileName: Pinned).MeasureAsync(
            track.Audio,
            TestContext.Current.CancellationToken
        );

    private sealed record CorpusTrack(string Audio, JsonElement Row)
    {
        private double[][] Markers =>
            [
                .. Row.GetProperty("markers")
                    .EnumerateArray()
                    .Select(marker => marker.EnumerateArray().Select(n => n.GetDouble()).ToArray()),
            ];

        public double BeatAt(double seconds) =>
            Markers is [[var s0, var b0], [var s1, var b1], ..]
                ? b0 + (seconds - s0) * (b1 - b0) / (s1 - s0)
                : throw new InvalidOperationException("a steady warp has two markers");

        /// <summary>How far the time sits from the warp's nearest beat, positive late.</summary>
        public double GridErrorMs(double seconds) =>
            Markers is [[var s0, var b0], [var s1, var b1], ..]
                ? (BeatAt(seconds) - Math.Round(BeatAt(seconds)))
                    * 60_000
                    * (s1 - s0)
                    / (b1 - b0)
                    / 60
                : throw new InvalidOperationException("a steady warp has two markers");

        /// <summary>How far in beats the time sits from a downbeat, taking the clip start as one, 4 to a bar.</summary>
        public double BarOffsetFromClipStart(double seconds)
        {
            var beats = BeatAt(seconds) - BeatAt(Row.GetProperty("clip_start_s").GetDouble());

            return beats - 4 * Math.Round(beats / 4);
        }
    }
}
