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

        var staged = ProcessedTracks.Scan(KeyLedger.Read(_paths.Ledger), [_paths.SampleStaging]);

        var discarded = held.Where(videoId => IsFinishedWith(videoId, staged)).ToList();

        foreach (var videoId in discarded)
            workingSet.Discard(videoId);

        return discarded.Count;
    }

    /// <remarks>
    /// Both halves are required: "nothing in staging" is also true of a track never separated,
    /// and of one only partly imported.
    /// </remarks>
    private bool IsFinishedWith(string videoId, ProcessedTracks staged) =>
        jobs.TryGet(videoId, JobStage.Separation, out var job)
        && job is { Status: JobStatus.Succeeded }
        && staged.StemsFor(videoId) is { Count: 0 }
        // A source a re-cut fetched is copied on the next pass; sweeping it first starts a loop.
        && !StageEligibility.Owes(jobs, videoId, JobStage.Handoff);
}
