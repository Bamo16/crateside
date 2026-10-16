using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class LookalikesRealDataTests
{
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    /// <summary>The two cases #41 measured on 2026-09-24, older identity first.</summary>
    private static readonly string[] KnownTwins =
    [
        "moby - south side (feat. gwen stefani)",
        "nimino - better (feat. manta)",
        "nimino, manta - better",
        "moby - south side",
    ];

    /// <summary>A song whose name finds a different, later song: each pair shows up once this way.</summary>
    [Fact]
    public void Only_the_measured_twins_look_like_another_song()
    {
        Assert.SkipUnless(File.Exists(LedgerPath), "The keytag ledger must be present.");

        var ledger = TestLedger.From(LedgerPath);

        var mistaken = ledger
            .Latest.Where(run =>
                Lookalikes.TryFind(run.Song, ledger, out var like)
                && !like.Song.Equals(run.Song, StringComparison.OrdinalIgnoreCase)
            )
            .Select(run => run.Song)
            .ToArray();

        Assert.All(mistaken, song => Assert.Contains(song, KnownTwins));
        Assert.Equal(2, mistaken.Length);
    }
}
