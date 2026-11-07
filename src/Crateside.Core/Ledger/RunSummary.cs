using Humanizer;

namespace Crateside.Core.Ledger;

/// <summary>The one line under Track details' heading.</summary>
public static class RunSummary
{
    public static string Of(RunEvidence run) =>
        (run.Sources.Count, run.Algorithms.Count) switch
        {
            (0, 0) => "No human sources and no detector readings; the chord check only.",
            // A ruling makes no chord check (ADR 0076).
            (0, var detectors) when run.DecidedBy is DecidedBy.Rules =>
                $"No human sources; {"detector".ToQuantity(detectors)}.",
            (0, var detectors) =>
                $"No human sources; {"detector".ToQuantity(detectors)} and the chord check.",
            (var sources, 0) =>
                $"{Agreeing(run)} of {"source".ToQuantity(sources)} agree; no detector readings.",
            (var sources, var detectors) =>
                $"{Agreeing(run)} of {"source".ToQuantity(sources)} agree, with {"detector".ToQuantity(detectors)} alongside.",
        };

    private static int Agreeing(RunEvidence run) =>
        run.Sources.Count(source => source.Stance is SourceStance.Agrees);
}
