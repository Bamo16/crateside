using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class RunSummaryTests
{
    private static readonly AlgorithmResult[] Detectors =
    [
        new() { Name = "madmom", Key = "A minor" },
        new() { Name = "Essentia edma", Key = "C major" },
    ];

    private static RunEvidence Run(
        AlgorithmResult[] algorithms,
        EvidenceSource[]? sources = null
    ) =>
        new()
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            Algorithms = algorithms,
            Sources = sources ?? [],
        };

    [Fact]
    public void A_run_with_no_detector_readings_does_not_count_zero_detectors()
    {
        var summary = RunSummary.Of(Run([]));

        Assert.DoesNotContain("0 detectors", summary);
        Assert.Contains("no detector readings", summary);
    }

    [Fact]
    public void A_run_with_sources_but_no_readings_says_so_without_counting_zero()
    {
        var summary = RunSummary.Of(Run([], [new()]));

        Assert.Equal("0 of 1 source agree; no detector readings.", summary);
    }

    [Fact]
    public void A_ruling_with_no_sources_claims_no_chord_check() =>
        Assert.Equal(
            "No human sources; 2 detectors.",
            RunSummary.Of(Run(Detectors) with { DecidedBy = DecidedBy.Rules })
        );

    [Fact]
    public void A_run_with_readings_counts_them() =>
        Assert.Equal(
            "No human sources; 2 detectors and the chord check.",
            RunSummary.Of(Run(Detectors))
        );
}
