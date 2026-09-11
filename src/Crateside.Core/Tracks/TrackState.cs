namespace Crateside.Core.Tracks;

/// <summary>
/// Where a track has got to, as one value.
/// </summary>
/// <remarks>Derived, never stored — nothing writes a state down.</remarks>
public enum TrackState
{
    /// <summary>In the playlist, with nothing run for it yet.</summary>
    Queued,

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

    /// <summary>Separated, and parked for want of a key worth tagging with.</summary>
    /// <remarks>Nothing automatic moves these on — the way out is a better verdict.</remarks>
    Parked,

    /// <summary>A stage failed enough times that it stopped trying.</summary>
    GaveUp,
}
