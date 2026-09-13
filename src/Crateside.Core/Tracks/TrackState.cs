namespace Crateside.Core.Tracks;

/// <summary>
/// Where a track has got to, as one value.
/// </summary>
/// <remarks>Derived, never stored — nothing writes a state down.</remarks>
public enum TrackState
{
    /// <summary>In the playlist, with nothing run for it yet.</summary>
    Queued,

    /// <summary>Fetching a source: the one to detect and cut from, or one the user added.</summary>
    Downloading,

    /// <summary>The evidence run is in flight: acquiring the audio and running the detectors.</summary>
    Detecting,

    /// <summary>The verdict run is in flight — the one stage that needs an agent.</summary>
    Judging,

    /// <summary>Judged, and waiting its turn on the GPU. Separation is strictly one at a time.</summary>
    JudgedInLine,

    /// <summary>On the GPU now. Single-flight, and the reason nothing else may start.</summary>
    Separating,

    /// <summary>The handoff is in flight: moving finished stems into <c>Sample Staging</c>.</summary>
    Staging,

    /// <summary>Writing the settled key onto the staged files as Ableton tags.</summary>
    Tagging,

    /// <summary>
    /// Staged and tagged: every automatic stage is done and the human-limited steps are next.
    /// </summary>
    /// <remarks>Not a queue — implies no urgency; a track can rest here for weeks.</remarks>
    ReadyToWarp,

    /// <summary>
    /// Every sample has reached <c>Mashup Samples</c>: warped, tagged, and in the finished library.
    /// </summary>
    /// <remarks>The move is the signal — nothing has to be marked done.</remarks>
    Done,

    /// <summary>
    /// crateside stopped and it is the user's turn. <see cref="ParkReason"/> says what over.
    /// </summary>
    /// <remarks>
    /// Nothing automatic moves these on. Not a failure and not a standing instruction — a
    /// finding, which the user resolves rather than clears (ADR 0015).
    /// </remarks>
    Parked,

    /// <summary>A stage failed enough times that it stopped trying.</summary>
    GaveUp,
}

/// <summary>
/// Why a track is parked, which is the axis that says what resolving it takes.
/// </summary>
/// <remarks>
/// The two sit at opposite ends of the pipeline: one has had the GPU spent on it and the other
/// has had nothing but a download.
/// </remarks>
public enum ParkReason
{
    /// <summary>Separated, with no verdict good enough to tag with. The way out is a better verdict.</summary>
    NoKey,

    /// <summary>
    /// The download came back short of premium audio, and was stopped before the verdict.
    /// </summary>
    /// <remarks>
    /// The way out is a better upload or the user's say-so. Only ever a shortfall about the
    /// upload itself — a dead or non-premium session is true of every download at once and never
    /// parks a single track (ADR 0016).
    /// </remarks>
    PremiumShortfall,
}
