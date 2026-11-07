using Crateside.Core.Pairing;

namespace Crateside.Tests.Pairing;

public sealed class RePitchTests
{
    [Theory]
    [InlineData(2, 143.68)]
    [InlineData(-1, 120.82)]
    [InlineData(0, 128.00)]
    [InlineData(12, 256.00)]
    public void Re_pitching_128_by_a_shift_gives_the_issues_figures(int semitones, double bpm) =>
        Assert.Equal(bpm, RePitch.ArrangementBpm(128, semitones), 2);

    [Theory]
    [InlineData("F minor", 2, "G minor")]
    [InlineData("F minor", -3, "D minor")]
    [InlineData("B major", 1, "C major")]
    [InlineData("C major", -1, "B major")]
    public void A_key_moves_by_the_shift_and_keeps_its_mode(
        string from,
        int semitones,
        string to
    ) => Assert.Equal(MusicKey.FromString(to), MusicKey.FromString(from)!.Transposed(semitones));

    [Theory]
    [InlineData("95,41", 95.41)]
    [InlineData(" 128 ", 128.0)]
    [InlineData("80.3904", 80.3904)]
    public void A_typed_bpm_reads_with_a_comma_or_a_point(string text, double bpm)
    {
        Assert.True(RePitch.TryParseBpm(text, out var parsed));
        Assert.Equal(bpm, parsed.Value, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fast")]
    [InlineData("0")]
    [InlineData("-120")]
    [InlineData("1000")]
    public void An_unreadable_bpm_is_refused(string text) =>
        Assert.False(RePitch.TryParseBpm(text, out _));
}
