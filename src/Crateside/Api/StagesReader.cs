using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;

namespace Crateside.Api;

public interface IStagesReader
{
    Task<StagesView> ReadAsync(CancellationToken ct);
}

/// <summary>Every worker's state and, for each track waiting on it, what its selector decided (#168).</summary>
public sealed class StagesReader(
    WorkListService workLists,
    LedgerStore ledgerStore,
    JobStore jobs,
    DownloadSelector download,
    EvidenceSelector evidence,
    VerdictSelector verdict,
    SeparationSelector separation,
    HandoffSelector handoff,
    TaggingSelector tagging,
    DrainControl separationControl,
    VerdictControl verdictControl
) : IStagesReader
{
    public async Task<StagesView> ReadAsync(CancellationToken ct)
    {
        var view = await workLists.ReadAsync(ct);
        var ledger = ledgerStore.Read();

        return new StagesView
        {
            Stages =
            [
                Describe(JobStage.Download, download.Decisions(view), null, false),
                Describe(JobStage.Evidence, evidence.Decisions(view), null, false),
                Describe(
                    JobStage.Verdict,
                    verdict.Decisions(view, ledger),
                    verdictControl.Hold?.Reason,
                    verdictControl.IsPaused,
                    verdictControl.Hold?.RetryAt
                ),
                Describe(
                    JobStage.Separation,
                    separation.Decisions(view),
                    null,
                    separationControl.IsPaused
                ),
                Describe(JobStage.Handoff, handoff.Decisions(), null, false),
                Describe(JobStage.Tagging, tagging.Decisions(ledger), null, false),
            ],
        };
    }

    private StageView Describe(
        JobStage stage,
        IReadOnlyList<Decision> decisions,
        string? holdReason,
        bool paused,
        DateTimeOffset? heldUntil = null
    )
    {
        var running = jobs.Open(stage).Select(job => job.TrackKey).ToList();
        var position = 0;
        var tracks = decisions
            .Select(decision =>
                decision.Selection is Selection.Skipped(var cause)
                    ? new StageTrack
                    {
                        VideoId = decision.TrackKey,
                        Title = decision.Title,
                        Picked = false,
                        Reason = cause,
                    }
                    : new StageTrack
                    {
                        VideoId = decision.TrackKey,
                        Title = decision.Title,
                        Picked = true,
                        Position = ++position,
                    }
            )
            .ToList();

        return new StageView
        {
            Stage = stage,
            State =
                holdReason is not null ? StageState.Held
                : paused ? StageState.Paused
                : running.Count > 0 ? StageState.Running
                : StageState.Idle,
            HeldBecause = holdReason,
            HeldUntil = heldUntil,
            Running = running,
            Next = tracks.FirstOrDefault(track => track.Picked),
            Waiting = tracks,
        };
    }
}

public sealed record StagesView
{
    public required IReadOnlyList<StageView> Stages { get; init; }
}

public sealed record StageView
{
    public required JobStage Stage { get; init; }

    public required StageState State { get; init; }

    public string? HeldBecause { get; init; }

    public DateTimeOffset? HeldUntil { get; init; }

    /// <summary>The video IDs the stage has a job open for.</summary>
    public required IReadOnlyList<string> Running { get; init; }

    /// <summary>What the stage would take next, when it is allowed to run.</summary>
    public StageTrack? Next { get; init; }

    /// <summary>Every track counted as waiting for this stage, picked or not.</summary>
    public required IReadOnlyList<StageTrack> Waiting { get; init; }
}

public sealed record StageTrack
{
    public required string VideoId { get; init; }

    public required string Title { get; init; }

    /// <summary>Whether the stage's selector will take it, in order.</summary>
    public required bool Picked { get; init; }

    /// <summary>Its place in the stage's line when picked.</summary>
    public int? Position { get; init; }

    /// <summary>Why the selector passes it over, when it does.</summary>
    public SkipCause? Reason { get; init; }
}

public enum StageState
{
    Idle,
    Running,
    Paused,
    Held,
}
