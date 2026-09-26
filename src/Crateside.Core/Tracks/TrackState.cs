namespace Crateside.Core.Tracks;

/// <summary>
/// Where a track has got to, as one value — derived, never stored.
/// </summary>
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
    ReadyToWarp,

    /// <summary>
    /// Every staged sample has its default clip saved, so warping is finished and the move is next.
    /// </summary>
    ReadyToMove,

    /// <summary>
    /// Every sample has reached <c>Mashup Samples</c>: warped, tagged, and in the finished library.
    /// </summary>
    Done,

    /// <summary>
    /// crateside stopped over a finding the user resolves; <see cref="ParkReason"/> says what
    /// (ADR 0015).
    /// </summary>
    Parked,

    /// <summary>A stage failed enough times that it stopped trying.</summary>
    GaveUp,

    /// <summary>Cut once, and missing files its plan asks for. A re-cut is the user's to ask for.</summary>
    Short,
}

/// <summary>
/// Why a track is parked, which is the axis that says what resolving it takes.
/// </summary>
public enum ParkReason
{
    /// <summary>Separated, with no verdict good enough to tag with. The way out is a better verdict.</summary>
    NoKey,

    /// <summary>
    /// The upload itself came back short of premium audio, and was stopped before the verdict.
    /// The way out is a better upload or the user's say-so (ADR 0016).
    /// </summary>
    PremiumShortfall,
}
