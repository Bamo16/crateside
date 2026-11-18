using Crateside.Core;
using Crateside.Core.KeyDetection;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Pairing;

namespace Crateside.Tests.KeyDetection;

public sealed class KeydetectFilterTests : IDisposable
{
    private const double MajorThird = 1.2599;
    private const double MinorThird = 1.1892;
    private const double Fifth = 1.4983;

    /// <summary>Am, Dm, E, Am: the major V names A minor rather than its relative C major.</summary>
    private static readonly (double Root, double Third)[] Progression =
    [
        (220.00, MinorThird),
        (293.66, MinorThird),
        (329.63, MajorThird),
        (220.00, MinorThird),
    ];

    private readonly string _folder = Directory
        .CreateTempSubdirectory("crateside-keydetect")
        .FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task The_pinned_builds_output_is_read_into_a_key_and_a_progression()
    {
        var pinned = RealPaths.PinnedBuild(FfmpegFilter.Keydetect);
        var audio = Path.Combine(_folder, "a-minor.wav");
        TestWav.Write(audio, 48, Chords);
        var filter = new KeydetectFilter(new ProcessRunner(), pinned.Path);

        var (version, sha256) = await filter.IdentifyAsync(TestContext.Current.CancellationToken);
        var reading = await filter.ReadAsync(
            audio,
            window: null,
            TestContext.Current.CancellationToken
        );

        Assert.StartsWith("ffmpeg version ", version);
        Assert.Equal(pinned.Build.Sha256, sha256, ignoreCase: true);

        var read = Assert.IsType<KeydetectReading.Read>(reading);

        Assert.Equal(MusicKey.FromString("A minor"), read.Key);
        Assert.InRange(read.R, 0.1, 1);
        Assert.True(read.Chords.Count > 8);
        Assert.Equal(read.Chords.Count, read.ChordStarts!.Count);
        Assert.Equal(read.ChordStarts.Order(), read.ChordStarts);
        Assert.NotNull(read.RunnerUp);
        Assert.NotNull(read.Reranked);
    }

    [Fact]
    public void Each_chord_keeps_the_start_the_filter_printed_for_it()
    {
        const string Stderr = """
            [Parsed_keydetect_0 @ 0] t=0.89 chord=F start=0.00
            [Parsed_keydetect_0 @ 0] t=3.14 chord=A# start=1.50
            [Parsed_keydetect_0 @ 0] t=4.19 chord=Gm start=3.10
            [Parsed_keydetect_0 @ 0] lavfi.keydetect.key=F
            [Parsed_keydetect_0 @ 0] lavfi.keydetect.chords=F-A#-Gm
            """;

        var read = Assert.IsType<KeydetectReading.Read>(KeydetectFilter.Parse(Stderr));

        Assert.Equal(["F", "A#", "Gm"], read.Chords);
        Assert.Equal([0.0, 1.5, 3.1], read.ChordStarts);
    }

    [Fact]
    public void A_long_track_keeps_the_starts_of_the_chords_the_summary_still_lists()
    {
        string[] names = ["C", "G", "Am", "F"];
        var events = Enumerable
            .Range(0, 70)
            .Select(i =>
                $"[Parsed_keydetect_0 @ 0] t={i + 1}.00 chord={names[i % 4]} start={i}.50"
            );
        var listed = Enumerable.Range(6, 64).Select(i => names[i % 4]);
        var stderr = string.Join(
            '\n',
            [
                .. events,
                "[Parsed_keydetect_0 @ 0] lavfi.keydetect.key=C",
                $"[Parsed_keydetect_0 @ 0] lavfi.keydetect.chords={string.Join('-', listed)}",
            ]
        );

        var read = Assert.IsType<KeydetectReading.Read>(KeydetectFilter.Parse(stderr));

        Assert.Equal(64, read.Chords.Count);
        Assert.Equal(Enumerable.Range(6, 64).Select(i => i + 0.5), read.ChordStarts);
    }

    [Fact]
    public void A_build_that_prints_no_starts_leaves_them_empty()
    {
        const string Stderr = """
            [Parsed_keydetect_0 @ 0] lavfi.keydetect.key=F
            [Parsed_keydetect_0 @ 0] lavfi.keydetect.chords=F-A#-Gm
            """;

        var read = Assert.IsType<KeydetectReading.Read>(KeydetectFilter.Parse(Stderr));

        Assert.Empty(read.ChordStarts!);
    }

    [Fact]
    public void The_tuning_the_filter_prints_is_read_with_its_sign()
    {
        const string Stderr = """
            [Parsed_keydetect_0 @ 0] lavfi.keydetect.key=F
            [Parsed_keydetect_0 @ 0] lavfi.keydetect.tuning_cents=-17.5
            """;

        var read = Assert.IsType<KeydetectReading.Read>(KeydetectFilter.Parse(Stderr));

        Assert.Equal(-17.5, read.TuningCents);
    }

    [Fact]
    public void A_build_that_prints_no_tuning_leaves_it_null()
    {
        const string Stderr = "[Parsed_keydetect_0 @ 0] lavfi.keydetect.key=F";

        var read = Assert.IsType<KeydetectReading.Read>(KeydetectFilter.Parse(Stderr));

        Assert.Null(read.TuningCents);
    }

    /// <summary>Each chord for two seconds: a triad over its root an octave down, each tone with two overtones.</summary>
    private static double Chords(double seconds)
    {
        var (root, third) = Progression[(int)(seconds / 2) % Progression.Length];

        return 0.12
            * (
                Tone(root, seconds)
                + Tone(root * third, seconds)
                + Tone(root * Fifth, seconds)
                + 1.2 * Tone(root / 2, seconds)
            );
    }

    private static double Tone(double hz, double seconds) =>
        Math.Sin(2 * Math.PI * hz * seconds)
        + 0.5 * Math.Sin(4 * Math.PI * hz * seconds)
        + 0.25 * Math.Sin(6 * Math.PI * hz * seconds);
}
