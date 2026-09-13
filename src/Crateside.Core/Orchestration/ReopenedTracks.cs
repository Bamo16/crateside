using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Tracks with a stage left open by something other than the work finishing — what a re-cut or
/// a source swap puts back in line.
/// </summary>
public static class ReopenedTracks
{
    /// <summary>The stages a track can be put back to. Evidence and verdict are never redone here.</summary>
    private static readonly JobStage[] Reopenable =
    [
        JobStage.Separation,
        JobStage.Handoff,
        JobStage.Tagging,
    ];

    /// <summary>
    /// One video ID per song still owing work, the most recently touched of that song's.
    /// </summary>
    /// <remarks>
    /// Grouped by song, not by video ID (ADR 0014). A source swap cancels the old ID's stages
    /// and completes them under the new one, so reading this per video ID leaves the old ID
    /// owing a handoff and a tagging that have in fact already happened — for ever.
    /// </remarks>
    public static IReadOnlySet<string> From(IEnumerable<TrackJob> jobs, KeyLedger ledger) =>
        Owing(jobs, ledger, Reopenable);

    /// <summary>
    /// The same, narrowed to songs owing a <em>separation</em>.
    /// </summary>
    /// <remarks>
    /// The only thing that may put a track the playlist still holds back in the line for the
    /// GPU. The wider set above also carries tracks owing a handoff or a tagging, which need no
    /// GPU and whose rows already exist — reading it here would show them as waiting for a
    /// separator that is not going to touch them.
    /// </remarks>
    public static IReadOnlySet<string> AwaitingCut(IEnumerable<TrackJob> jobs, KeyLedger ledger) =>
        Owing(jobs, ledger, [JobStage.Separation]);

    private static IReadOnlySet<string> Owing(
        IEnumerable<TrackJob> jobs,
        KeyLedger ledger,
        JobStage[] stages
    )
    {
        string Identity(TrackJob job) => ledger.SongFor(job.VideoId) ?? job.VideoId;

        return jobs.Where(job => stages.Contains(job.Stage))
            .GroupBy(Identity, StringComparer.Ordinal)
            .Where(song => song.GroupBy(job => job.Stage).Select(Latest).Any(IsOwed))
            .Select(song => Latest(song).VideoId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static TrackJob Latest(IEnumerable<TrackJob> jobs) => jobs.MaxBy(job => job.UpdatedAt)!;

    private static bool IsOwed(TrackJob job) =>
        job.Status is JobStatus.Cancelled or JobStatus.Queued or JobStatus.Running;
}
