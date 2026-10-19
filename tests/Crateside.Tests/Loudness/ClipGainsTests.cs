using Crateside.Core.Loudness;

namespace Crateside.Tests.Loudness;

public sealed class ClipGainsTests
{
    private static readonly LoudnessOptions Defaults = new();

    [Theory]
    [InlineData(-20.0, 6.0)]
    [InlineData(-8.4, -5.6)]
    [InlineData(-14.0, 0.0)]
    [InlineData(-23.9, 9.9)]
    public void A_sample_is_given_the_gain_that_brings_it_to_the_target(double lufs, double db) =>
        Assert.Equal(db, Decibels(Wanted("Song (Vocal - Full).flac", lufs)), 3);

    [Fact]
    public void A_stem_below_the_floor_is_left_at_0_dB() =>
        Assert.Equal(1, Wanted("Johnny Pearson - Action in Memphis (Vocal - Full).flac", -70));

    [Fact]
    public void Drums_are_left_at_0_dB_however_quiet() =>
        Assert.Equal(1, Wanted("Song (Drums).flac", -17.4));

    [Fact]
    public void A_file_ffmpeg_could_not_read_has_no_gain() =>
        Assert.Null(
            ClipGains.Wanted("Song (Vocal - Full).flac", new LoudnessReading(null), Defaults)
        );

    [Fact]
    public void The_target_and_floor_are_settings() =>
        Assert.Equal(
            (-4, 4),
            (
                Math.Round(Decibels(Wanted("Song (Vocal - Full).flac", -14, Settings)), 3),
                Math.Round(Decibels(Wanted("Song (Vocal - Full).flac", -22, Settings)), 3)
            )
        );

    [Fact]
    public void A_gain_within_a_twentieth_of_a_dB_is_on_target()
    {
        var wanted = (float)Math.Pow(10, 6.0 / 20);

        Assert.True(ClipGains.IsOnTarget((float)Math.Pow(10, 6.04 / 20), wanted));
        Assert.False(ClipGains.IsOnTarget((float)Math.Pow(10, 6.2 / 20), wanted));
        Assert.False(ClipGains.IsOnTarget(0, wanted));
    }

    private static readonly LoudnessOptions Settings = new() { TargetLufs = -18, FloorLufs = -30 };

    private static float Wanted(string name, double lufs, LoudnessOptions? options = null) =>
        ClipGains.Wanted(name, new LoudnessReading(lufs), options ?? Defaults) is { } gain
            ? gain
            : throw new InvalidOperationException("No gain for a file with a reading");

    private static double Decibels(float gain) => 20 * Math.Log10(gain);
}
