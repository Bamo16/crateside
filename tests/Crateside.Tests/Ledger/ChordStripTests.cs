using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class ChordStripTests
{
    private static RunEvidence Run(string[] chords, double[] starts) =>
        new()
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            Chords = chords,
            ChordStarts = starts,
        };

    private static string Drawn(IReadOnlyList<ChordBand> bands) =>
        string.Join(" | ", bands.Select(band => $"{band.Start:0.#}-{band.End:0.#} {band.Label}"));

    [Fact]
    public void A_chord_runs_to_the_next_ones_start_and_the_last_to_the_end_of_the_track() =>
        Assert.Equal(
            "0-1.5 F | 1.5-3 A♯ | 3-10 G♯m",
            Drawn(ChordStrip.Plan(Run(["F", "A#", "G#m"], [0, 1.5, 3]), 10))
        );

    [Fact]
    public void No_chord_leaves_a_gap_rather_than_a_block() =>
        Assert.Equal("0-1 F | 2-4 C", Drawn(ChordStrip.Plan(Run(["F", "N", "C"], [0, 1, 2]), 4)));

    [Fact]
    public void A_run_recorded_without_starts_draws_nothing() =>
        Assert.Empty(ChordStrip.Plan(Run(["F", "C"], []), 10));

    [Fact]
    public void A_chord_past_the_end_of_the_track_is_dropped() =>
        Assert.Equal("0-5 F", Drawn(ChordStrip.Plan(Run(["F", "C"], [0, 8]), 5)));
}
