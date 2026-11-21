using Crateside.Core.SourceOffsets;

namespace Crateside.Tests.SourceOffsets;

public sealed class OffsetMeasurerTests
{
    private const int Rate = 16_000;

    private static readonly float[] Mix = Noise(40, seed: 1);

    [Fact]
    public void A_source_with_its_first_three_seconds_cut_reads_aligned_three_seconds_in()
    {
        var reading = Measure(Mix[(3 * Rate)..]);

        Assert.Equal(OffsetVerdict.Aligned, reading.Verdict);
        Assert.Equal(3 * Rate, reading.Offset);
    }

    [Fact]
    public void A_source_padded_with_half_a_second_of_silence_reads_aligned_the_other_way()
    {
        var reading = Measure([.. new float[Rate / 2], .. Mix]);

        Assert.Equal(OffsetVerdict.Aligned, reading.Verdict);
        Assert.Equal(-Rate / 2, reading.Offset);
    }

    [Fact]
    public void A_source_missing_head_and_tail_reads_aligned()
    {
        var reading = Measure(Mix[(7 * Rate)..(33 * Rate)]);

        Assert.Equal(OffsetVerdict.Aligned, reading.Verdict);
        Assert.Equal(7 * Rate, reading.Offset);
    }

    [Fact]
    public void A_source_with_a_section_taken_out_reads_as_an_edit_with_two_runs()
    {
        var reading = Measure([.. Mix[..(15 * Rate)], .. Mix[(20 * Rate)..]]);

        Assert.Equal(OffsetVerdict.Edit, reading.Verdict);
        Assert.Contains(reading.Runs, run => run.Offset == 0);
        Assert.Contains(reading.Runs, run => run.Offset == 5 * Rate);
    }

    [Fact]
    public void A_source_running_sixty_ppm_fast_reads_drift_with_the_ppm_it_runs_at()
    {
        var reading = Measure(Resampler.Resample(Mix, 1_000_000, 1_000_060));

        Assert.Equal(OffsetVerdict.Drift, reading.Verdict);
        Assert.Null(reading.SpeedFactor);
        Assert.InRange(Math.Abs(reading.DriftPpm!.Value), 45, 75);
    }

    [Fact]
    public void A_source_a_thousandth_slow_reads_drift_with_that_speed_factor()
    {
        var reading = Measure(Resampler.Resample(Mix, 1001, 1000));

        Assert.Equal(OffsetVerdict.Drift, reading.Verdict);
        Assert.Equal("1000/1001", reading.SpeedFactor);
    }

    [Fact]
    public void Unrelated_audio_reads_as_not_the_same_recording()
    {
        var reading = Measure(Noise(40, seed: 2));

        Assert.Equal(OffsetVerdict.NotTheSameRecording, reading.Verdict);
        Assert.Null(reading.Offset);
    }

    [Fact]
    public void A_silent_source_reads_as_not_the_same_recording()
    {
        Assert.Equal(OffsetVerdict.NotTheSameRecording, Measure(new float[30 * Rate]).Verdict);
    }

    private static OffsetReading Measure(float[] source) =>
        OffsetMeasurer.Measure(Mix, source, Rate, TestContext.Current.CancellationToken);

    /// <summary>Noise whose loudness swells and fades, so every window has its own character.</summary>
    private static float[] Noise(int seconds, int seed)
    {
        var random = new Random(seed);
        var samples = new float[seconds * Rate];

        for (var i = 0; i < samples.Length; i++)
            samples[i] =
                (float)(random.NextDouble() - 0.5)
                * (0.4f + (0.6f * (float)Math.Abs(Math.Sin(i / (Rate * 1.7)))));

        return samples;
    }
}
