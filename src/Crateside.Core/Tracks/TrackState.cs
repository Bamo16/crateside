namespace Crateside.Core.Tracks;

/// <summary>
/// Where a track has got to, as one value.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the thing crateside did not model.</strong> A track's state was implicit in
/// which bucket it fell into — the queue view's work lists, the job store's per-stage records,
/// and which root its files were sitting in — so the same track appeared in a different place on
/// screen every time it moved on. Naming the state makes it a column rather than an address.
/// </para>
/// <para>
/// Derived, never stored. Nothing writes a state down; every member below is a reading of the
/// ledger, the job store, and what is on disk.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// The only state that is genuinely the user's turn, and the one a track rests in for weeks.
    /// It is not a queue and implies no urgency.
    /// </remarks>
    ReadyToWarp,

    /// <summary>
    /// Every sample has reached <c>Mashup Samples</c>: warped, tagged, and in the finished library.
    /// </summary>
    /// <remarks>
    /// The move is the signal. It is the user's own action in Ableton, it covers warping and
    /// tagging and renaming together, and it is self-cleaning — nothing has to be marked.
    /// </remarks>
    Done,

    /// <summary>
    /// Separated, and parked for want of a key worth tagging with.
    /// </summary>
    /// <remarks>
    /// Nothing crateside runs will move these on: the way out is a better verdict, which is a
    /// human decision. A resting place rather than a queue, and it carries no urgency.
    /// </remarks>
    Parked,

    /// <summary>A stage failed enough times that it stopped trying.</summary>
    GaveUp,

    /// <summary>The user set it aside — usually the wrong upload of the right song.</summary>
    Held,
}
