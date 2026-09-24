using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Throws away the working set of every track that has been <strong>imported</strong>.
/// </summary>
public sealed class WorkingSetJanitor(
    WorkingSet workingSet,
    JobStore jobs,
    IOptions<CratesidePaths> paths
)
{
    private readonly CratesidePaths _paths = paths.Value;

    /// <summary>Discards what is finished with, and reports how many went.</summary>
    public int Sweep()
    {
        if (workingSet.VideoIds is not { Count: > 0 } held)
            return 0;

        var ledger = KeyLedger.Read(_paths.Ledger);
        var staged = ProcessedTracks.Scan(ledger, [_paths.SampleStaging]);

        List<string> discarded = [.. held.Where(videoId => IsFinishedWith(videoId, staged))];

        // Opening every file in the library is costly, so only when something could be judge-only.
        if (
            held.Except(discarded).Where(videoId => MayBeJudgeOnly(videoId, ledger)).ToList() is
            { Count: > 0 } candidates
        )
        {
            var finished = ProcessedTracks.Scan(
                ledger,
                [_paths.SampleStaging, _paths.MashupSamples]
            );

            discarded.AddRange(candidates.Where(finished.IsFullyProcessed));
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
        && !StageEligibility.Owes(jobs, videoId, JobStage.Handoff);

    /// <summary>
    /// Judged, and never given a separation: a track queued only to judge files the library
    /// already holds (#47), once those files carry its video ID.
    /// </summary>
    private bool MayBeJudgeOnly(string videoId, KeyLedger ledger) =>
        ledger.HasRunFor(videoId)
        && !jobs.TryGet(videoId, JobStage.Separation, out _)
        && !StageEligibility.Owes(jobs, videoId, JobStage.Handoff);
}
