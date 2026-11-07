using System.Globalization;
using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Records a ruling for a track whose evidence and pages settle its key, before any judge is asked (ADR 0076).</summary>
public sealed class VerdictByRules(
    LedgerStore store,
    WorkingSet workingSet,
    SourceIndex sources,
    PageLookups lookups,
    TimeProvider time
)
{
    /// <summary>The recorded ruling, or null when the rules leave the track open.</summary>
    public async Task<VerdictResult?> TryRuleAsync(WorkOrder order, CancellationToken ct = default)
    {
        if (!workingSet.TryReadEvidence(order.TrackKey, out var evidence))
            return null;

        var found = await lookups.LookUpAsync(order, ct);

        if (
            lookups.NamesOf(order) is not var (artist, title)
            || !VerdictRules.TryRule(evidence, found, out var ruling)
        )
            return null;

        var audio = sources.PathFor(order);
        var held = lookups.HeldRun(order);

        if (
            store.Record(
                new RunRecord
                {
                    RecordedAt = time.GetUtcNow(),
                    Song = held?.Song ?? LedgerVocabulary.SongIdentity(artist, title),
                    Artist = artist,
                    Title = LedgerVocabulary.WithoutStemSuffix(title),
                    SourceUrl = order.SourceUrl,
                    InputPath = audio,
                    InputKind = audio is null ? null : "full_mix",
                    Confidence = ruling.Confidence,
                    DecidedBy = DecidedBy.Rules,
                    Center = ruling.Center,
                    Sources = ruling.Sources,
                    Candidates = ruling.Candidates,
                    Reasoning = ruling.Reasoning,
                    Evidence = LateReading.Unstamped(evidence),
                    Lookups = VerdictRecording.Serialized(found),
                }
            )
            is RecordOutcome.Refused(var field, var problem)
        )
            return VerdictResult.Failed($"the ledger refused the ruling: {field}: {problem}");

        VerdictRecording.LinkSwappedSource(store, order);

        return VerdictResult.Succeeded(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{ruling.Center.Name}, {ruling.Confidence.ToString().ToUpperInvariant()}, ruled by crateside"
            )
        );
    }
}
