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
        Assert.NotNull(read.RunnerUp);
        Assert.NotNull(read.Reranked);
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
