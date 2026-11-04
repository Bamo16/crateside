using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Warps;
using Crateside.Core.WorkLists;
using Humanizer;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Works down the stems waiting to be handed to the human half, one track at a time.
/// </summary>
public sealed class HandoffDrainer(
    LedgerStore store,
    HandoffRunner runner,
    HandoffSelector selector,
    JobStore jobs,
    IOptions<CratesidePaths> paths,
    TrackInstructionStore instructions,
    ResolvedNameStore names,
    DeletionLog? deletions = null,
    HandoffSidecars? sidecars = null
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Handoff;

    public async Task<StageTick> DrainOnceAsync(CancellationToken ct = default)
    {
        var ledger = store.Read();

        StemLanding.ReleaseParked(_paths, ledger, jobs, deletions);

        if (!selector.TryPickNext(out var trackKey, out var stems))
            return StageTick.Idle;

        var subject = ledger.TryGetLatestByTrackKey(trackKey, out var run) ? run.Song : trackKey;

        jobs.Record(trackKey, JobStage.Handoff, JobStatus.Running);

        // Worked out before the move, so each stem lands in staging with its sidecar (#77).
        var prepared = sidecars is null
            ? new Dictionary<string, PreparedSidecar>()
            : await sidecars.PrepareAsync(stems, ct);

        var result = runner.Run(
            stems,
            NameFor(trackKey),
            SupersededStems.In(_paths.StagingFolder, trackKey, ledger, Kept()),
            (from, to) => sidecars?.Moved(prepared, from, to)
        );

        jobs.Record(
            trackKey,
            JobStage.Handoff,
            result.IsSuccess ? JobStatus.Succeeded : JobStatus.Failed,
            error: result.Error,
            summary: result.IsSuccess ? Summarize(result) : null
        );

        return StageTick.Ran(subject, result.IsSuccess, result.Error, Cleared(result.Cleared));
    }

    /// <summary>What a finished handoff moved, for the activity feed (#170).</summary>
    internal static string Summarize(HandoffResult result) =>
        result.Cleared is 0
            ? $"staged {"stem".ToQuantity(result.Moved)}"
            : $"staged {"stem".ToQuantity(result.Moved)}, removed {"superseded file".ToQuantity(result.Cleared)}";

    /// <summary>
    /// Said out loud because crateside deleted the user's audio to make room, and the only other
    /// sign of it is samples quietly ceasing to appear in the row (ADR 0014).
    /// </summary>
    private static string? Cleared(int count) =>
        count is 0 ? null : $"{"superseded file".ToQuantity(count)} removed from Staging";

    /// <summary>Every upload the user has asked to keep, across all tracks, and so never superseded.</summary>
    private HashSet<string> Kept() =>
        instructions
            .Read()
            .SelectMany(track => track.Plan?.AddedSources ?? [])
            .Select(source => source.TrackKey)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// What the user calls the track this file belongs to — through its owner, for an added
    /// source — or null when they have not said (ADR 0014).
    /// </summary>
    private string? NameFor(string trackKey) =>
        Owner(trackKey) switch
        {
            null => null,
            { Name: { Length: > 0 } stated } => stated,
            // A track's own download already arrives under the name the separator was asked for.
            { TrackKey: var owner } when owner.Equals(trackKey, StringComparison.Ordinal) => null,
            { TrackKey: var owner } => names.Read().GetValueOrDefault(owner)?.BaseName,
        };

    private TrackInstruction? Owner(string trackKey) =>
        instructions
            .Read()
            .FirstOrDefault(track =>
                track.TrackKey.Equals(trackKey, StringComparison.Ordinal)
                || track.Plan?.AddedSources.Any(source =>
                    source.TrackKey.Equals(trackKey, StringComparison.Ordinal)
                )
                    is true
            );
}
