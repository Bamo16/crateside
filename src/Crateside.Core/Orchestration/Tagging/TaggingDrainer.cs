using System.Diagnostics;
using Crateside.Core.Ableton;
using Crateside.Core.Audit;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Humanizer;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Tagging;

/// <summary>
/// Works down the files in staging that have a verdict and no key applied, one track at a time.
/// </summary>
public sealed class TaggingDrainer(
    LedgerStore store,
    TaggingRunner runner,
    TaggingSelector selector,
    JobStore jobs,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Tagging;

    public Task<StageTick> DrainOnceAsync(CancellationToken ct = default)
    {
        var ledger = store.Read();

        if (!selector.TryPickNext(ledger, out var trackKey, out var stems))
            return Task.FromResult(StageTick.Idle);

        var subject = stems[0].Song ?? trackKey;

        jobs.Record(trackKey, JobStage.Tagging, JobStatus.Running);

        var outcome = runner.Run(stems);

        // A stale write carries no strike, the same as a spent quota.
        var (status, error) = outcome switch
        {
            TagWriteOutcome.Written => (JobStatus.Succeeded, (string?)null),
            TagWriteOutcome.Stale(var stale) => (JobStatus.Canceled, stale),
            TagWriteOutcome.Rejected(var rejected) => (JobStatus.Failed, rejected),
            _ => throw new UnreachableException(),
        };

        jobs.Record(
            trackKey,
            JobStage.Tagging,
            status,
            error: error,
            facts: outcome is TagWriteOutcome.Written(var applied)
                ? new TaggingFacts(applied, KeyOf(stems))
                : null
        );

        return Task.FromResult(StageTick.Ran(subject, outcome is TagWriteOutcome.Written, error));
    }

    /// <summary>The one key every file carries, or null when they differ.</summary>
    internal static string? KeyOf(IReadOnlyList<TaggedStem> stems) =>
        stems.Select(stem => stem.LedgerKey).Distinct().ToList() is [{ } key] ? key : null;
}
