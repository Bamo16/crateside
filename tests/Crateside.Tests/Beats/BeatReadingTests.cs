using System.Globalization;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Library;

namespace Crateside.Tests.Beats;

public class BeatReadingTests
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

        Assert.Equal(new Detected(173.99, 0.144, 344.86), detected);
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
                type is null
                    ? null
                    : new AbletonFileTags
                    {
                        RelativePath = file,
                        Keywords = [],
                        Type = type,
                    }
            )
        );

    [Fact]
    public void A_kept_source_used_as_the_instrumental_is_read_too()
    {
        var paths = new CratesidePaths();
        Assert.SkipUnless(
            Directory.Exists(paths.MashupSamples),
            $"{paths.MashupSamples} not present."
        );

        Assert.Contains(
            BeatReader.Instrumentals(paths, new AbletonFolderInfoReader()),
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
        var audio = Path.Combine(
            @"P:\RYAN\Ableton\Mashup Samples",
            "1991 - If Only (Instrumental - Full).flac"
        );
        Assert.SkipUnless(File.Exists(audio), $"{audio} not present on this machine.");

        var reader = new BeatReader();
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
