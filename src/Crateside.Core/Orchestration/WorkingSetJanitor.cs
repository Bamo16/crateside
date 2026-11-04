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
        if (workingSet.TrackKeys is not { Count: > 0 } held)
            return 0;

        var running = jobs.All()
            .Where(job => job.Status is JobStatus.Running)
            .Select(job => job.TrackKey)
            .ToHashSet();

        // Its evidence and pages are what the person decides from.
        var waiting = jobs.All()
            .Where(job => job is { Stage: JobStage.Verdict, Status: JobStatus.AwaitingPerson })
            .Select(job => job.TrackKey)
            .ToHashSet();

        held =
        [
            .. held.Where(trackKey => !running.Contains(trackKey) && !waiting.Contains(trackKey)),
        ];

        var ledger = store.Read();
        var staged = ProcessedTracks.Scan(ledger, [_paths.StagingFolder]);
        var (keeping, now) = (retention.Read(), time.GetUtcNow());
        var everywhere = new Lazy<ProcessedTracks>(() =>
            ProcessedTracks.Scan(ledger, [_paths.StagingFolder, _paths.LibraryFolder])
        );

        List<string> discarded = [];

        foreach (var trackKey in held.Where(trackKey => IsFinishedWith(trackKey, staged)))
        {
            if (workingSet.LeftStagingAt(trackKey) is not { } left)
                workingSet.RecordLeftStaging(trackKey, left = now);

            // The library already holds this audio (the same decode, MEASURED in #133).
            if (
                keeping.HasLapsed(left, now)
                || everywhere.Value.StemsFor(trackKey).Any(stem => stem.IsVerbatimSource)
            )
                discarded.Add(trackKey);
        }

        // Back in staging: the clock restarts when the stems leave again.
        foreach (
            var trackKey in held.Where(trackKey => staged.StemsFor(trackKey) is { Count: > 0 })
        )
            workingSet.ClearLeftStaging(trackKey);

        // Opening every file in the library is costly, so only when something could be judge-only.
        // Its clock starts once the library files carry its video ID, unless a backfill started it first.
        foreach (
            var trackKey in held.Except(discarded)
                .Where(trackKey => MayBeJudgeOnly(trackKey, ledger))
                .Where(trackKey => everywhere.Value.IsFullyProcessed(trackKey))
                .ToList()
        )
        {
            if (workingSet.LeftStagingAt(trackKey) is not { } left)
                workingSet.RecordLeftStaging(trackKey, left = now);

            if (
                keeping.HasLapsed(left, now)
                || everywhere.Value.StemsFor(trackKey).Any(stem => stem.IsVerbatimSource)
            )
                discarded.Add(trackKey);
        }

        foreach (var trackKey in discarded)
            workingSet.Discard(trackKey);

        return discarded.Count;
    }

    /// <summary>
    /// Separated and nothing left in staging — either alone is also true of unfinished tracks.
    /// </summary>
    private bool IsFinishedWith(string trackKey, ProcessedTracks staged) =>
        jobs.TryGet(trackKey, JobStage.Separation, out var job)
        && job is { Status: JobStatus.Succeeded }
        && staged.StemsFor(trackKey) is { Count: 0 }
        // A source a re-cut fetched is copied on the next pass; sweeping it first starts a loop.
        && !StageEligibility.Owes(jobs, trackKey, JobStage.Handoff)
        && IsSettled(trackKey);

    private bool IsSettled(string trackKey) =>
        CutRequests.IsSettled(
            instructions
                ?.Read()
                .FirstOrDefault(track => track.TrackKey.Equals(trackKey, StringComparison.Ordinal))
                ?.CutRequestedAt,
            jobs.TryGet(trackKey, JobStage.Handoff, out var handoff) ? handoff : null
        );

    /// <summary>
    /// Judged, and never given a separation: a track queued only to judge files the library
    /// already holds (#47), once those files carry its video ID.
    /// </summary>
    private bool MayBeJudgeOnly(string trackKey, KeyLedger ledger) =>
        ledger.HasRunFor(trackKey)
        && !jobs.TryGet(trackKey, JobStage.Separation, out _)
        && !StageEligibility.Owes(jobs, trackKey, JobStage.Handoff);
}
