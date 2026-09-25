using Crateside.Core.Ledger;
using Crateside.Core.Naming;
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
    QueueSettingsStore settings,
    ResolvedNameStore names,
    DeletionLog? deletions = null
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Handoff;

    public Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        var ledger = KeyLedger.Read(_paths.Ledger);

        StemLanding.ReleaseParked(_paths, ledger, jobs, deletions);

        if (!selector.TryPickNext(out var videoId, out var stems))
            return Task.FromResult(StageTick.Idle);

        var subject = ledger.TryGetLatestByVideoId(videoId, out var run) ? run.Song : videoId;

        jobs.Record(videoId, JobStage.Handoff, JobStatus.Running);

        var result = runner.Run(
            stems,
            NameFor(videoId),
            SupersededStems.In(_paths.SampleStaging, videoId, ledger, Kept())
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

    /// <summary>Every upload the user has asked to keep, across all tracks, and so never superseded.</summary>
    private IReadOnlySet<string> Kept() =>
        settings
            .Read()
            .Tracks.SelectMany(track => track.Plan?.AddedSources ?? [])
            .Select(source => source.VideoId)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// What the user calls the track this file belongs to — through its owner, for an added
    /// source — or null when they have not said (ADR 0014).
    /// </summary>
    private string? NameFor(string videoId) =>
        Owner(videoId) switch
        {
            null => null,
            { Name: { Length: > 0 } stated } => stated,
            // A track's own download already arrives under the name the separator was asked for.
            { VideoId: var owner } when owner.Equals(videoId, StringComparison.Ordinal) => null,
            { VideoId: var owner } => names.Read().GetValueOrDefault(owner)?.BaseName,
        };

    private TrackInstruction? Owner(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(track =>
                track.VideoId.Equals(videoId, StringComparison.Ordinal)
                || track.Plan?.AddedSources.Any(source =>
                    source.VideoId.Equals(videoId, StringComparison.Ordinal)
                )
                    is true
            );
}
