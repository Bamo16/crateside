using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Works down the stems waiting to be handed to the human half, one track at a time.
/// </summary>
public sealed class HandoffDrainer(
    HandoffRunner runner,
    HandoffSelector selector,
    JobStore jobs,
    IOptions<CratesidePaths> paths,
    QueueSettingsStore settings
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Handoff;

    public Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!selector.TryPickNext(out var videoId, out var stems))
            return Task.FromResult(StageTick.Idle);

        var subject = KeyLedger.Read(_paths.Ledger).TryGetLatestByVideoId(videoId, out var run)
            ? run.Song
            : videoId;

        jobs.Record(videoId, JobStage.Handoff, JobStatus.Running);

        var result = runner.Run(stems, NameFor(videoId));

        jobs.Record(
            videoId,
            JobStage.Handoff,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error
        );

        return Task.FromResult(StageTick.Ran(subject, result.IsSuccess, result.Error));
    }

    private string? NameFor(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(track => track.VideoId.Equals(videoId, StringComparison.Ordinal))
            ?.Name;
}
