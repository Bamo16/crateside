using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works down the files in staging that have a verdict and no key applied, one track at a time.
/// </summary>
public sealed class TaggingDrainer(
    TaggingRunner runner,
    TaggingSelector selector,
    JobStore jobs,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Tagging;

    public Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        var ledger = KeyLedger.Read(_paths.Ledger);

        if (!selector.TryPickNext(ledger, out var videoId, out var stems))
            return Task.FromResult(StageTick.Idle);

        var subject = stems[0].Song ?? videoId;

        jobs.Record(videoId, JobStage.Tagging, JobStatus.Running);

        var outcome = runner.Run(stems);

        // A stale write carries no strike, the same as a spent quota.
        jobs.Record(
            videoId,
            JobStage.Tagging,
            outcome switch
            {
                { IsSuccess: true } => JobStatus.Succeeded,
                { IsStale: true } => JobStatus.Cancelled,
                _ => JobStatus.Failed,
            },
            error: outcome.Error
        );

        return Task.FromResult(StageTick.Ran(subject, outcome.IsSuccess, outcome.Error));
    }
}
