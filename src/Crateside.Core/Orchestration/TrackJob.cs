namespace Crateside.Core.Orchestration;

/// <summary>
/// What crateside has done, or is doing, about one track at one <see cref="JobStage"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a <em>projection</em> over the appended <see cref="JobUpdate"/> lines, not a stored
/// row: the latest update for an id wins, exactly as a supersede works in the ledger.
/// </para>
/// <para>
/// <strong>This is the first non-derived state in the system, and it is deliberately narrow.</strong>
/// It records what crateside <em>ran</em> — never what is true of the library. Whether a track
/// has a verdict still comes from the ledger, and whether its stems are finished still means
/// they left <c>Sample Staging</c>. A job that says "succeeded" is not evidence that a track is
/// done; it is only evidence that crateside tried.
/// </para>
/// </remarks>
public sealed record TrackJob
{
    /// <summary>Stable across attempts: <c>{videoId}:{stage}</c>. Retrying does not mint a new id.</summary>
    public required string Id { get; init; }

    public required string VideoId { get; init; }
    public required JobStage Stage { get; init; }
    public required JobStatus Status { get; init; }

    /// <summary>When the latest update landed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// The separation process, while one is running. Null for every other stage — only
    /// separation is single-flight and long enough to outlive a restart.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// The OS-reported start time of <see cref="ProcessId"/>. Carried because a PID alone is
    /// not an identity: Windows reuses them, so a live process with a matching PID but a
    /// different start time is somebody else's, not ours to adopt.
    /// </summary>
    public DateTimeOffset? ProcessStartedAt { get; init; }

    /// <summary>Why the last failure failed, for the UI and for the morning after.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// How many times this job has failed, counted over its whole history. Derived rather than
    /// stored, so it cannot drift from the record it summarizes. What stops a poisoned track
    /// from being retried every night forever — though what counts as too many is the
    /// scheduler's call, not the store's.
    /// </summary>
    public required int Strikes { get; init; }

    /// <summary>Whether crateside currently believes work is in flight for this job.</summary>
    public bool IsOpen => Status is JobStatus.Queued or JobStatus.Running;
}

/// <summary>
/// Which kind of work a job is. Named separately because each contends for a <em>different</em>
/// scarce resource — nothing, Claude quota, and the GPU — and so each wants its own moment.
/// </summary>
public enum JobStage
{
    /// <summary>Acquire the audio and run the detectors. Deterministic; contends for nothing.</summary>
    Evidence,

    /// <summary>Turn evidence into a verdict. The only stage that needs an agent.</summary>
    Verdict,

    /// <summary>Separation. GPU-bound, serial, hours long, and strictly single-flight.</summary>
    Separation,

    /// <summary>
    /// Hands the finished stems to the human half by moving them into <c>Sample Staging</c>.
    /// A file move, so it contends for nothing — like evidence, it needs no moment picked for it.
    /// </summary>
    Handoff,

    /// <summary>
    /// Applies the settled key to the files in staging, as Ableton tags. Contends for nothing,
    /// though it shares a file with a running Ableton — see ADR 0003.
    /// </summary>
    Tagging,
}

/// <summary>How a job is currently going.</summary>
public enum JobStatus
{
    /// <summary>Chosen to run, not yet started.</summary>
    Queued,
    Running,
    Succeeded,

    /// <summary>Failed and eligible to be retried, subject to <see cref="TrackJob.Strikes"/>.</summary>
    Failed,

    /// <summary>Stopped on purpose. Not a strike — the user's call is not the track's fault.</summary>
    Cancelled,
}
