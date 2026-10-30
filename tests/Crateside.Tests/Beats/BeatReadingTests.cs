using System.Globalization;
using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Library;
using Crateside.Core.Orchestration.Loop;
using Crateside.Tests.Ableton;

namespace Crateside.Tests.Beats;

public sealed class BeatReadingTests
{
    private static readonly string Corpus = Environment.SpecialFolder.UserProfile.GetFolderPath(
        "Music",
        "Stems",
        "BPM Corpus"
    );

    [Fact]
    public void Windows_that_agree_to_a_few_hundredths_do_not_drift() =>
        Assert.False(
            new BeatReading { Windows = [174.07, 174.02, 173.99, 173.97, 173.99, 173.99] }.Drifts
        );

    [Fact]
    public void A_window_read_at_double_tempo_is_left_out_rather_than_folded_in()
    {
        double[] steady = [97.16, 97.2, 97.18, 97.21, 97.17];

        Assert.Equal(BeatReading.Spread(steady), BeatReading.Spread([.. steady, 194.9]));
    }

    [Fact]
    public void Under_three_windows_in_one_octave_says_nothing() =>
        Assert.Null(BeatReading.Spread([120, 121, 60.2]));

    [Fact]
    public void The_spread_matches_the_corpus_script_on_every_track_it_scored()
    {
        var scored = Path.Combine(Corpus, "2026-09-28 steadiness", "windows-octave.tsv");
        Assert.SkipUnless(File.Exists(scored), $"{scored} not present on this machine.");

        var lines = File.ReadAllLines(scored);
        var header = lines[0].Split('\t');
        int Column(string name) => Array.IndexOf(header, name);

        var compared = 0;

        foreach (var fields in lines.Skip(1).Select(line => line.Split('\t')))
        {
            double[] windows =
            [
                .. fields[Column("readings")]
                    .Split(' ')
                    .Select(bpm => double.Parse(bpm, CultureInfo.InvariantCulture)),
            ];
            var expected = fields[Column("iqr2")] is { Length: > 0 } iqr
                ? double.Parse(iqr, CultureInfo.InvariantCulture)
                : (double?)null;

            var spread = BeatReading.Spread(windows);

            Assert.Equal(expected is null, spread is null);
            if (expected is { } value)
                Assert.Equal(value, spread!.Value, 0.0006);

            compared++;
        }

        Assert.True(compared > 400, $"only {compared} tracks compared");
    }

    [Fact]
    public void The_last_value_of_each_figure_is_the_reading()
    {
        var detected = BeatReader.Parse(
            """
            frame:1    pts:4096    pts_time:0.0928
            lavfi.beatdetect.bpm=0.00
            lavfi.beatdetect.confidence=0.000
            frame:2    pts:8192    pts_time:0.1857
            lavfi.beatdetect.bpm=173.99
            lavfi.beatdetect.confidence=0.144
            lavfi.beatdetect.beat_interval_ms=344.86
            lavfi.beatdetect.beat_offset_ms=31.6
            """
        );

        Assert.Equal(173.99, detected.Bpm);
        Assert.Equal(0.144, detected.Confidence);
        Assert.Equal(344.86, detected.BeatIntervalMs);
        Assert.Null(detected.TempoSpread);
    }

    [Fact]
    public void The_final_frame_of_a_1_1_build_carries_its_spread_at_full_precision()
    {
        var detected = BeatReader.Parse(
            """
            frame:2569 pts:10522624 pts_time:238.608254
            lavfi.beatdetect.bpm=183.24584
            lavfi.beatdetect.confidence=0.126
            lavfi.beatdetect.beat_interval_ms=327.4290
            lavfi.beatdetect.beat_offset_ms=26.06
            lavfi.beatdetect.tempo_spread=2.234
            lavfi.beatdetect.candidate0=184.224:0.148:234.6
            lavfi.beatdetect.final=1
            lavfi.beatdetect.version=1.1.0
            """
        );

        Assert.Equal(183.24584, detected.Bpm);
        Assert.Equal(0.126, detected.Confidence);
        Assert.Equal(327.4290, detected.BeatIntervalMs);
        Assert.Equal(2.234, detected.TempoSpread);
    }

    [Fact]
    public void A_1_3_build_final_frame_gives_the_grid_downbeat_candidates_and_segments()
    {
        var detected = BeatReader.Parse(
            """
            frame:3070 pts:12574720 pts_time:285.141043
            lavfi.beatdetect.bpm=180.35088
            lavfi.beatdetect.tempo_segment2=0:1:99
            frame:3071 pts:12578816 pts_time:285.233923
            lavfi.beatdetect.bpm=134.99356
            lavfi.beatdetect.confidence=0.195
            lavfi.beatdetect.beat_interval_ms=444.4656
            lavfi.beatdetect.beat_offset_ms=60.75
            lavfi.beatdetect.tempo_spread=0.139
            lavfi.beatdetect.candidate0=67.436:0.136:306.5
            lavfi.beatdetect.candidate1=135.087:0.163:221.0
            lavfi.beatdetect.tempo_segment0=0:262777:135.00671
            lavfi.beatdetect.tempo_segment1=262777:285222:135.66048
            lavfi.beatdetect.tempo_wander=0.571
            lavfi.beatdetect.downbeat_offset_ms=1394.15
            lavfi.beatdetect.beats_per_bar=4
            lavfi.beatdetect.downbeat_confidence=0.029
            lavfi.beatdetect.final=1
            """
        );

        Assert.Equal(60.75, detected.BeatOffsetMs);
        Assert.Equal(1394.15, detected.DownbeatOffsetMs);
        Assert.Equal(0.029, detected.DownbeatConfidence);
        Assert.Equal(4, detected.BeatsPerBar);
        Assert.Equal(0.571, detected.TempoWander);
        Assert.Equal(
            [new BeatCandidate(67.436, 0.136, 306.5), new BeatCandidate(135.087, 0.163, 221.0)],
            detected.Candidates
        );
        Assert.Equal(
            [new TempoSegment(0, 262777, 135.00671), new TempoSegment(262777, 285222, 135.66048)],
            detected.Segments
        );
    }

    [Fact]
    public void No_tempo_reported_reads_as_none() =>
        Assert.Null(BeatReader.Parse("lavfi.beatdetect.bpm=0.00").Bpm);

    [Theory]
    [InlineData(191.7, new[] { 0, 30, 60, 90, 120, 150 })]
    [InlineData(60, new[] { 0, 30 })]
    [InlineData(12, new[] { 0 })]
    public void Only_full_windows_are_read(double seconds, int[] starts) =>
        Assert.Equal(starts, BeatReader.WindowStarts(seconds));

    [Theory]
    [InlineData("Darude - Sandstorm (Radio Edit).flac", "Instrumental", true)]
    [InlineData("Adele - Hello (Instrumental - Full).flac", "Instrumental", true)]
    [InlineData("Adele - Hello (Instrumental - Full).flac", "Vocal", false)]
    [InlineData("Adele - Hello (Instrumental - Full).flac", null, true)]
    [InlineData("Adele - Hello (Drums).flac", null, false)]
    public void Ableton_s_type_decides_and_the_name_only_when_there_is_no_type(
        string file,
        string? type,
        bool instrumental
    ) =>
        Assert.Equal(
            instrumental,
            BeatReader.IsInstrumental(
                file,
                type is null ? null : TestTags.Of(file, [$"Type|{type}"])
            )
        );

    [Fact]
    public void A_kept_source_used_as_the_instrumental_is_read_too()
    {
        var paths = RealPaths.Create();
        Assert.SkipUnless(
            Directory.Exists(paths.LibraryFolder),
            $"{paths.LibraryFolder} not present."
        );

        Assert.Contains(
            BeatReader.Instrumentals(paths, new FolderInfoReader()),
            file => Path.GetFileName(file) == "Darude - Sandstorm (Radio Edit).flac"
        );
    }

    [Fact]
    public void The_drift_flag_is_counted_against_each_warp_shape()
    {
        LibrarySong[] songs =
        [
            Song(TempoShape.Steady, [128, 128.01, 128, 128.01]),
            Song(TempoShape.Drifting, [96, 97, 98, 99]),
            Song(TempoShape.Drifting, [96, 96, 96, 96]),
            Song(TempoShape.Drifting, [96]),
        ];

        Assert.Equal(
            [
                new DriftCount(TempoShape.Steady, 0, 1),
                new DriftCount(TempoShape.MostlySteady, 0, 0),
                new DriftCount(TempoShape.Drifting, 1, 2),
            ],
            DriftCount.From(songs)
        );
    }

    [Fact]
    public async Task A_real_instrumental_reads_at_its_warped_tempo_and_is_kept_until_it_changes()
    {
        var audio = Path.Combine(RealPaths.Library(), "1991 - If Only (Instrumental - Full).flac");
        Assert.SkipUnless(File.Exists(audio), $"{audio} not present on this machine.");

        var reader = new BeatReader(new ProcessRunner());
        Assert.NotNull(await reader.RefreshBuildAsync(TestContext.Current.CancellationToken));

        var reading = await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);

        Assert.Equal(174, reading.Bpm!.Value, 0.5);
        Assert.Equal(6, reading.Windows.Count);
        Assert.False(reading.Drifts);
        Assert.Same(reading, reader.Read(audio));
    }

    private static LibrarySong Song(TempoShape shape, double[] windows) =>
        new()
        {
            Song = Guid.NewGuid().ToString("n"),
            Name = "song",
            Tempo = new WarpTempo(windows[0], shape, windows.Min(), windows.Max(), []),
            Beats = new BeatReading { Windows = windows },
        };
}
