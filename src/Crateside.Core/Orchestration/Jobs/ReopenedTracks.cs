using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Jobs;

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
    /// One video ID per song still owing work, the most recently touched of that song's — by song,
    /// since a swap finishes the old ID's stages under the new one (ADR 0014).
    /// </summary>
    public static IReadOnlySet<string> From(IEnumerable<TrackJob> jobs, KeyLedger ledger) =>
        Owing(jobs, ledger, Reopenable);

    /// <summary>
    /// The same, narrowed to songs owing a <em>separation</em> — the only set that may put a held
    /// track back in line for the GPU.
    /// </summary>
    public static IReadOnlySet<string> AwaitingCut(IEnumerable<TrackJob> jobs, KeyLedger ledger) =>
        Owing(jobs, ledger, [JobStage.Separation]);

    private static HashSet<string> Owing(
        IEnumerable<TrackJob> jobs,
        KeyLedger ledger,
        JobStage[] stages
    )
    {
        string Identity(TrackJob job) => ledger.SongFor(job.VideoId) ?? job.VideoId;

        return jobs.Where(job => stages.Contains(job.Stage))
            .GroupBy(Identity, StringComparer.Ordinal)
            .Where(song =>
                song.GroupBy(job => job.Stage).Select(Latest).Any(StageEligibility.IsOwed)
            )
            .Select(song => Latest(song).VideoId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static TrackJob Latest(IEnumerable<TrackJob> jobs) => jobs.MaxBy(job => job.UpdatedAt)!;
}
