using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Pairing;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Library;

public sealed class KeyDetectorCountsTests
{
    private const string Ledger = @"V:\Repos\keytag\results\runs.jsonl";

    [Theory]
    [InlineData("A minor", "A minor", KeyMiss.Right)]
    [InlineData("E minor", "A minor", KeyMiss.Fifth)]
    [InlineData("D minor", "A minor", KeyMiss.Fifth)]
    [InlineData("C major", "A minor", KeyMiss.Relative)]
    [InlineData("A major", "A minor", KeyMiss.Parallel)]
    [InlineData("G major", "A minor", KeyMiss.Other)]
    [InlineData(null, "A minor", KeyMiss.NoReading)]
    public void A_reading_is_placed_by_how_it_relates_to_the_verdict(
        string? read,
        string verdict,
        KeyMiss miss
    ) => Assert.Equal(miss, KeyMisses.Of(MusicKey.FromString(read), MusicKey.FromString(verdict)!));

    [Fact]
    public void The_ledger_gives_keydetect_the_lead_and_every_song_one_place_per_detector()
    {
        Assert.SkipUnless(File.Exists(Ledger), $"{Ledger} not present on this machine.");

        var counts = KeyDetectorCounts.From(
            TestLedger.LatestEvidence(Ledger).Values,
            TestLedger.From(Ledger),
            highOnly: false
        );

        Assert.True(counts.Songs > 390, $"only {counts.Songs} songs");
        Assert.All(
            counts.Detectors,
            tally => Assert.Equal(counts.Songs, tally.ByMiss.Values.Sum())
        );
        Assert.Equal(
            ["keydetect", "crema", "librosa"],
            counts
                .Detectors.OrderByDescending(tally => tally.Count(KeyMiss.Right))
                .Select(tally => tally.Name)
        );
        Assert.NotEmpty(counts.AllWrong);
    }
}
