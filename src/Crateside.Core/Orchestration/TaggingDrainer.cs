using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works down the files in staging that have a verdict and no key applied, one track at a time.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No pause, and no waiting for Ableton to close.</strong> ADR 0003 measured that an
/// external tag is picked up live and reaches the search index without a restart, so holding
/// writes back until Ableton quits would cost the user real time to avoid a window measured at
/// about a second, twice per move-out. The writer narrows that window instead.
/// </para>
/// <para>
/// Reads no queue, like handoff: tagging a file already in the library is no business of a
/// YouTube credential.
/// </para>
/// </remarks>
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

        // A stale write is the store moving underneath us, which says nothing about this track
        // and is fixed by trying again -- so it is recorded the way a spent quota is, carrying
        // no strike. Left as an ordinary failure, a busy Ableton could strike a track out of
        // ever being tagged.
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
