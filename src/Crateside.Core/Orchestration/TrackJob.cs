namespace Crateside.Core.Orchestration;

/// <summary>
/// What crateside has done, or is doing, about one track at one <see cref="JobStage"/>.
/// </summary>
/// <remarks>
/// A projection over the appended <see cref="JobUpdate"/> lines; the latest for an id wins. A
/// job that says "succeeded" records only that crateside tried, never what is true of the library.
/// </remarks>
public sealed record TrackJob
{
    /// <summary>Stable across attempts: <c>{videoId}:{stage}</c>. Retrying does not mint a new id.</summary>
    public required string Id { get; init; }

    public required string VideoId { get; init; }
    public required JobStage Stage { get; init; }
    public required JobStatus Status { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// The separation process, while one is running. Null for every other stage — only
    /// separation is single-flight and long enough to outlive a restart.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// The OS-reported start time of <see cref="ProcessId"/>.
    /// </summary>
    /// <remarks>
    /// A PID alone is not an identity: Windows reuses them, so a matching PID with a different
    /// start time is somebody else's.
    /// </remarks>
    public DateTimeOffset? ProcessStartedAt { get; init; }

    /// <summary>Why the last failure failed.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// How many times this job has failed, counted over its whole history. Derived rather than
    /// stored, so it cannot drift from the record it summarizes.
    /// </summary>
    public required int Strikes { get; init; }

    /// <summary>When a restarted crateside found this separation's process still alive.</summary>
    /// <remarks>Null once anything else is recorded: it describes the latest line only.</remarks>
    public DateTimeOffset? AdoptedAt { get; init; }

    /// <summary>Whether crateside currently believes work is in flight for this job.</summary>
    public bool IsOpen => Status is JobStatus.Queued or JobStatus.Running;
}

/// <summary>
/// Which kind of work a job is. Named separately because each contends for a different scarce
/// resource: nothing, Claude quota, or the GPU.
/// </summary>
public enum JobStage
{
    /// <summary>Fetch a source. Network-bound; contends for nothing (ADR 0019).</summary>
    Download,

    /// <summary>Run the detectors over audio already held. Deterministic; contends for nothing.</summary>
    Evidence,

    /// <summary>Turn evidence into a verdict. The only stage that needs an agent.</summary>
    Verdict,

    /// <summary>GPU-bound, serial, hours long, and strictly single-flight.</summary>
    Separation,

    /// <summary>
    /// Hands the finished stems to the human half by moving them into <c>Sample Staging</c>.
    /// A file move, so it contends for nothing.
    /// </summary>
    Handoff,

    /// <summary>
    /// Applies the settled key to the files in staging, as Ableton tags. Contends for nothing,
    /// though it shares a file with a running Ableton.
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
