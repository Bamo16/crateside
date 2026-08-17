using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Throws away the working set of every track that has been <strong>imported</strong>.
/// </summary>
/// <remarks>
/// <para>
/// Import is the point at which there is nothing further to do with a track, and therefore
/// nothing further to do with what was gathered about it. The same fact that says a song is
/// finished says its source is disposable.
/// </para>
/// <para>
/// Its own sweep rather than a step in a stage, because it is answering a question about the
/// <em>library</em> — has the user finished with this track — which no stage is watching and
/// which changes on human timescales rather than on a queue's.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Whether the user has finished with this track.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Both halves are required.</strong> "Nothing in staging" is also true of a track
    /// that has never been separated, so on its own it would delete the source of every track
    /// before its stems existed — including the one currently being detected.
    /// </para>
    /// <para>
    /// And it must be <em>nothing</em> rather than <em>no samples</em>: the manual workflow
    /// empties staging progressively — the warp guide is deleted mid-warp, the vocal is dragged
    /// out before the instrumental — so a partly imported song must not trip this.
    /// </para>
    /// </remarks>
    private bool IsFinishedWith(string videoId, ProcessedTracks staged) =>
        jobs.TryGet(videoId, JobStage.Separation, out var job)
        && job is { Status: JobStatus.Succeeded }
        && staged.StemsFor(videoId) is { Count: 0 };
}
