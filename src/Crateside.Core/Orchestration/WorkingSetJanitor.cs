using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Throws away the working set of every track that has been <strong>imported</strong>, or only judged, once its
/// source has been kept for the retention the user set (#133, ADR 0061).
/// </summary>
public sealed class WorkingSetJanitor(
    LedgerStore store,
    WorkingSet workingSet,
    JobStore jobs,
    IOptions<CratesidePaths> paths,
    SourceRetentionStore retention,
    TimeProvider time,
    TrackInstructionStore? instructions = null
)
{
    private readonly CratesidePaths _paths = paths.Value;

    /// <summary>Discards what is finished with and past its retention, and reports how many went.</summary>
    public int Sweep()
    {
        if (workingSet.VideoIds is not { Count: > 0 } held)
            return 0;

        var running = jobs.All()
            .Where(job => job.Status is JobStatus.Running)
            .Select(job => job.VideoId)
            .ToHashSet();

        held = [.. held.Where(videoId => !running.Contains(videoId))];

        var ledger = store.Read();
        var staged = ProcessedTracks.Scan(ledger, [_paths.StagingFolder]);
        var (keeping, now) = (retention.Read(), time.GetUtcNow());
        var everywhere = new Lazy<ProcessedTracks>(() =>
            ProcessedTracks.Scan(ledger, [_paths.StagingFolder, _paths.LibraryFolder])
        );

        List<string> discarded = [];

        foreach (var videoId in held.Where(videoId => IsFinishedWith(videoId, staged)))
        {
            if (workingSet.LeftStagingAt(videoId) is not { } left)
                workingSet.RecordLeftStaging(videoId, left = now);

            // The library already holds this audio (the same decode, MEASURED in #133).
            if (
                keeping.HasLapsed(left, now)
                || everywhere.Value.StemsFor(videoId).Any(stem => stem.IsVerbatimSource)
            )
                discarded.Add(videoId);
        }

        // Back in staging: the clock restarts when the stems leave again.
        foreach (var videoId in held.Where(videoId => staged.StemsFor(videoId) is { Count: > 0 }))
            workingSet.ClearLeftStaging(videoId);

        // Opening every file in the library is costly, so only when something could be judge-only.
        // Its clock starts once the library files carry its video ID, unless a backfill started it first.
        foreach (
            var videoId in held.Except(discarded)
                .Where(videoId => MayBeJudgeOnly(videoId, ledger))
                .Where(videoId => everywhere.Value.IsFullyProcessed(videoId))
                .ToList()
        )
        {
            if (workingSet.LeftStagingAt(videoId) is not { } left)
                workingSet.RecordLeftStaging(videoId, left = now);

            if (
                keeping.HasLapsed(left, now)
                || everywhere.Value.StemsFor(videoId).Any(stem => stem.IsVerbatimSource)
            )
                discarded.Add(videoId);
        }

        foreach (var videoId in discarded)
            workingSet.Discard(videoId);

        return discarded.Count;
    }

    /// <summary>
    /// Separated and nothing left in staging — either alone is also true of unfinished tracks.
    /// </summary>
    private bool IsFinishedWith(string videoId, ProcessedTracks staged) =>
        jobs.TryGet(videoId, JobStage.Separation, out var job)
        && job is { Status: JobStatus.Succeeded }
        && staged.StemsFor(videoId) is { Count: 0 }
        // A source a re-cut fetched is copied on the next pass; sweeping it first starts a loop.
        && !StageEligibility.Owes(jobs, videoId, JobStage.Handoff)
        && IsSettled(videoId);

    private bool IsSettled(string videoId) =>
        CutRequests.IsSettled(
            instructions
                ?.Read()
                .FirstOrDefault(track => track.VideoId.Equals(videoId, StringComparison.Ordinal))
                ?.CutRequestedAt,
            jobs.TryGet(videoId, JobStage.Handoff, out var handoff) ? handoff : null
        );

    /// <summary>
    /// Judged, and never given a separation: a track queued only to judge files the library
    /// already holds (#47), once those files carry its video ID.
    /// </summary>
    private bool MayBeJudgeOnly(string videoId, KeyLedger ledger) =>
        ledger.HasRunFor(videoId)
        && !jobs.TryGet(videoId, JobStage.Separation, out _)
        && !StageEligibility.Owes(jobs, videoId, JobStage.Handoff);
}
