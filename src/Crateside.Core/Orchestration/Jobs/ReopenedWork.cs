using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// Video IDs crateside has work to redo for, whatever the playlist says, decided per song
/// (ADR 0014).
/// </summary>
public interface IReopenedWork
{
    /// <summary>Songs owing any reopened stage, or a re-cut not yet settled.</summary>
    IReadOnlySet<string> Owing(KeyLedger ledger);

    /// <summary>Songs owing a separation: the only set that may put a held track back in line for the GPU.</summary>
    IReadOnlySet<string> AwaitingCut(KeyLedger ledger);
}

/// <summary>Reopened work as the jobs and the user's re-cut requests stand now.</summary>
public sealed class ReopenedWork(JobStore jobs, TrackInstructionStore instructions) : IReopenedWork
{
    public IReadOnlySet<string> Owing(KeyLedger ledger) =>
        ReopenedTracks
            .From(jobs.All(), ledger)
            .Union(CutRequests.Unsettled(instructions.Read(), jobs))
            .ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> AwaitingCut(KeyLedger ledger) =>
        ReopenedTracks
            .AwaitingCut(jobs.All(), ledger)
            .Union(CutRequests.Cutting(instructions.Read(), jobs))
            .ToHashSet(StringComparer.Ordinal);
}
