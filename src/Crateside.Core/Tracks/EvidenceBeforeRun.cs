using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Core.Tracks;

/// <summary>
/// What the Evidence stage left for a track no run has judged: the detectors' readings and the
/// pages the lookup found (ADR 0096).
/// </summary>
public sealed class EvidenceBeforeRun(WorkingSet workingSet, PageLookups lookups)
{
    /// <summary>The working set's evidence read as a run's, or null when the track has none.</summary>
    public RunEvidence? Read(string trackKey, string song, string? title)
    {
        if (!workingSet.TryReadEvidence(trackKey, out var evidence))
            return null;

        var pages = lookups.CachedFor(trackKey) is { } kept
            ? lookups.Effective(trackKey, kept)
            : [];

        return RunEvidence.From(
            new RunRecord
            {
                RecordedAt = File.GetLastWriteTimeUtc(workingSet.EvidencePathFor(trackKey)),
                Song = song,
                Title = title,
                Confidence = KeyConfidence.Withheld,
                Evidence = evidence,
                Lookups = VerdictRecording.Serialized(pages),
            }
        ) with
        {
            DecidedBy = null,
            RulesOpen = VerdictRules.Decide(evidence, pages) is RulesOutcome.Open(var why)
                ? why
                : null,
        };
    }
}
