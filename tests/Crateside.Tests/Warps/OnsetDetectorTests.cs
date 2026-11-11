using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class OnsetDetectorTests
{
    private const int Rate = 44_100;

    [Fact]
    public void Clicks_are_found_at_the_start_of_their_attack()
    {
        int[] attacks = [Rate / 2, Rate, 3 * Rate / 2, 2 * Rate];

        var found = new OnsetDetector().Detect(Clicks(attacks, seconds: 3));

        Assert.Equal(attacks.Length, found.Count);

        foreach (var (attack, transient) in attacks.Zip(found))
            Assert.InRange(transient.Position - attack, -0.005 * Rate, 0.005 * Rate);
    }

    [Fact]
    public void The_strongest_transient_has_energy_one_and_the_rest_less()
    {
        var found = new OnsetDetector().Detect(
            Clicks([Rate / 2, Rate], seconds: 2, levels: [0.9f, 0.3f])
        );

        Assert.Equal(1f, found[0].Energy);
        Assert.InRange(found[1].Energy, 0f, 0.99f);
    }

    [Fact]
    public void Silence_has_no_transients() =>
        Assert.Empty(
            new OnsetDetector().Detect(new DecodedAudio(new float[2 * Rate * 2], 2, Rate))
        );

    [Fact]
    public void A_stem_with_nothing_above_the_floor_has_no_transients_though_it_has_attacks()
    {
        int[] attacks = [Rate / 2, Rate, 3 * Rate / 2, 2 * Rate];
        var bleed = Clicks(attacks, seconds: 3, levels: [0.002f, 0.002f, 0.002f, 0.002f]);

        Assert.Empty(new OnsetDetector().Detect(bleed));
        Assert.True(
            new OnsetDetector(new OnsetSettings { FloorDb = float.NegativeInfinity })
                .Detect(bleed)
                .Count >= attacks.Length
        );
    }

    [Fact]
    public void A_quiet_hit_beside_a_loud_one_is_dropped_and_the_loud_one_stays_the_strongest()
    {
        var found = new OnsetDetector().Detect(
            Clicks([Rate / 2, Rate, 3 * Rate / 2], seconds: 3, levels: [0.8f, 0.002f, 0.5f])
        );

        Assert.Equal(
            [Rate / 2, 3 * Rate / 2],
            found.Select(hit => Math.Round(hit.Position / (0.1 * Rate)) * 0.1 * Rate)
        );
        Assert.Equal(1f, found.Max(hit => hit.Energy));
    }

    /// <summary>Stereo bursts of noise that rise over 2 ms and decay over 100 ms.</summary>
    private static DecodedAudio Clicks(int[] attacks, int seconds, float[]? levels = null)
    {
        var random = new Random(54);
        var samples = new float[seconds * Rate * 2];

        for (var a = 0; a < attacks.Length; a++)
        {
            var level = levels?[a] ?? 0.8f;

            for (var i = 0; i < Rate / 5 && attacks[a] + i < seconds * Rate; i++)
            {
                var envelope = Math.Min(1f, i / (0.002f * Rate)) * MathF.Exp(-i / (0.1f * Rate));
                var value = level * envelope * (float)(random.NextDouble() * 2 - 1);
                samples[(attacks[a] + i) * 2] += value;
                samples[(attacks[a] + i) * 2 + 1] += value;
            }
        }

        return new DecodedAudio(samples, 2, Rate);
    }
}
