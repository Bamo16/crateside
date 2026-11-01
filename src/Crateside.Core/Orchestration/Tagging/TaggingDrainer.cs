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

        if (!selector.TryPickNext(ledger, out var videoId, out var stems))
            return Task.FromResult(StageTick.Idle);

        var subject = stems[0].Song ?? videoId;

        jobs.Record(videoId, JobStage.Tagging, JobStatus.Running);

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
            videoId,
            JobStage.Tagging,
            status,
            error: error,
            summary: outcome is TagWriteOutcome.Written(var applied)
                ? Summarize(applied, stems)
                : null
        );

        return Task.FromResult(StageTick.Ran(subject, outcome is TagWriteOutcome.Written, error));
    }

    /// <summary>What a finished tagging pass wrote, for the activity feed (#170).</summary>
    internal static string Summarize(int applied, IReadOnlyList<TaggedStem> stems) =>
        stems.Select(stem => stem.LedgerKey).Distinct().ToList() is [{ } key]
            ? $"tagged {"file".ToQuantity(applied)} {key}"
            : $"tagged {"file".ToQuantity(applied)}";
}
