using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Humanizer;
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

        var ledger = KeyLedger.Read(_paths.Ledger);

        var subject = ledger.TryGetLatestByVideoId(videoId, out var run) ? run.Song : videoId;

        jobs.Record(videoId, JobStage.Handoff, JobStatus.Running);

        var result = runner.Run(
            stems,
            NameFor(videoId),
            SupersededStems.In(_paths.SampleStaging, videoId, ledger)
        );

        jobs.Record(
            videoId,
            JobStage.Handoff,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error
        );

        return Task.FromResult(
            StageTick.Ran(subject, result.IsSuccess, result.Error, Cleared(result.Cleared))
        );
    }

    /// <summary>
    /// Said out loud because crateside deleted the user's audio to make room, and the only other
    /// sign of it is samples quietly ceasing to appear in the row (ADR 0014).
    /// </summary>
    private static string? Cleared(int count) =>
        count is 0 ? null : $"{"superseded file".ToQuantity(count)} removed from Sample Staging";

    private string? NameFor(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(track => track.VideoId.Equals(videoId, StringComparison.Ordinal))
            ?.Name;
}
