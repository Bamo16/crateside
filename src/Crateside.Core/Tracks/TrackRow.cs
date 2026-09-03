using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Humanizer;

namespace Crateside.Core.Tracks;

/// <summary>
/// One track, with everything known about it rolled up: the row the Tracks page draws.
/// </summary>
/// <remarks>
/// <para>
/// A join across the queue item, the ledger verdict, what is on disk, and what crateside has
/// recorded running — reduced to one <see cref="State"/> and the one sentence that says what
/// happens next. Both the chip and the <c>Next</c> column read those two, so the copy cannot
/// drift apart from the state it describes.
/// </para>
/// <para>
/// Entirely derived, like <see cref="Staging.StagingSong"/> before it: nothing here is stored and
/// nothing is marked done by hand.
/// </para>
/// </remarks>
public sealed record TrackRow
{
    public required string VideoId { get; init; }

    /// <summary>The playlist entry, which is what removing the track targets.</summary>
    public required string PlaylistItemId { get; init; }

    public required string Title { get; init; }

    /// <summary>The current verdict, or null when the ledger has never seen this track.</summary>
    public LedgerRun? Verdict { get; init; }

    /// <summary>The key to tag with, or null when withheld, weak, or unknown.</summary>
    public string? Key => Verdict?.HasTaggableKey is true ? Verdict.Key : null;

    public KeyConfidence Confidence => Verdict?.Confidence ?? KeyConfidence.Unknown;

    /// <summary>
    /// The warp starting point, and the one number the user needs mid-warp.
    /// </summary>
    /// <remarks>
    /// Deliberately not gated on <see cref="Key"/>. A verdict too weak to tag with still measured
    /// a tempo, it was never a tagged value, and a parked track is exactly one the user may still
    /// choose to work by hand.
    /// </remarks>
    public double? Bpm => Verdict?.Bpm;

    /// <summary>
    /// The stage crateside currently believes is working this track, if any.
    /// </summary>
    /// <remarks>
    /// Reduced from the job store rather than carried whole: what the row needs is which stage
    /// has this track in flight, not five job records it would have to re-interrogate.
    /// </remarks>
    public JobStage? RunningStage { get; init; }

    /// <summary>The stage that ran out of strikes, if one has.</summary>
    /// <remarks>
    /// Reduced in one place so that <em>every</em> stage is checked. The queue page assembled this
    /// itself and left tagging out, so a track that gave up there said nothing at all.
    /// </remarks>
    public JobStage? GaveUpAt { get; init; }

    /// <summary>How many times <see cref="GaveUpAt"/> failed before it stopped trying.</summary>
    public int GaveUpStrikes { get; init; }

    /// <summary>The user has told crateside to leave this track alone.</summary>
    public bool IsHeld { get; init; }

    /// <summary>
    /// Separated, with its stems parked in the unresolved-key root for want of a verdict good
    /// enough to tag with.
    /// </summary>
    public bool IsParkedForKey { get; init; }

    /// <summary>Its place in the line for the GPU, counting from one. Null when it is not in it.</summary>
    public int? GpuPosition { get; init; }

    /// <summary>
    /// The files of this track meant to be kept — warp guides excluded, because they are used up
    /// rather than kept. Not because they are untagged: they carry the key like everything else.
    /// </summary>
    /// <remarks>
    /// For most tracks the vocal and instrumental stems; for an instrumental release, the source
    /// track, which is a sample and no kind of stem at all (ADR 0004).
    /// </remarks>
    public IReadOnlyList<TrackSample> Samples { get; init; } = [];

    /// <summary>
    /// A warp guide is still on disk, which says warping is outstanding.
    /// </summary>
    /// <remarks>
    /// A bool rather than a list because a guide has no per-file detail of its own: it carries
    /// the song's key like everything else, and the row already holds that. Its <em>absence</em>
    /// is not a claim that warping happened — that reading was measured at eight songs and is far
    /// too small to build on (ADR 0004).
    /// </remarks>
    public bool HasWarpGuide { get; init; }

    /// <summary>
    /// Where the track has got to.
    /// </summary>
    /// <remarks>
    /// Resolved in precedence order, and the order is the argument: <em>done</em> is read off disk
    /// and outranks everything, because a finished track is finished whatever crateside thinks it
    /// was doing. A stage actually in flight comes next — a hold governs what crateside will
    /// <em>start</em>, and six minutes of GPU already being spent is not something to describe as
    /// untouched. A hold then outranks the remaining off-ramps, <em>giving up included</em>:
    /// holding a track is the very thing <see cref="Next"/> suggests when a stage gives up, so it
    /// has to change what the row says or the advice leads nowhere.
    /// </remarks>
    public TrackState State =>
        this switch
        {
            { IsDone: true } => TrackState.Done,
            { RunningStage: { } stage } => InFlight(stage),
            { IsHeld: true } => TrackState.Held,
            { GaveUpAt: not null } => TrackState.GaveUp,
            { IsParkedForKey: true } => TrackState.Parked,
            { GpuPosition: not null } => TrackState.JudgedInLine,
            _ => AtRest,
        };

    /// <summary>
    /// What happens next, in plain English — or what is waiting on the user.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="State"/> alone, so the sentence and the chip can never disagree.
    /// </remarks>
    public string Next =>
        State switch
        {
            TrackState.Detecting => "then judging",
            TrackState.Judging => "verdict in about a minute",
            TrackState.JudgedInLine => InLine,
            TrackState.Separating => "then staging",
            TrackState.Staging => "moving stems into Sample Staging",
            TrackState.Tagging => "writing Ableton key tags",
            TrackState.ReadyToWarp => "your turn — warp, then move to Mashup Samples",
            TrackState.Parked => "needs a better verdict — nothing automatic will move it",
            TrackState.GaveUp => "retry, or hold it",
            TrackState.Held => "nothing will touch it until you release it",
            TrackState.Done => "in Mashup Samples",
            _ => "downloading next",
        };

    /// <summary>
    /// Every sample has left for <c>Mashup Samples</c>. The move is the signal — see
    /// <see cref="TrackSample.IsWarped"/>.
    /// </summary>
    private bool IsDone => Samples is { Count: > 0 } && Samples.All(s => s.IsWarped);

    private static TrackState InFlight(JobStage stage) =>
        stage switch
        {
            JobStage.Evidence => TrackState.Detecting,
            JobStage.Verdict => TrackState.Judging,
            JobStage.Separation => TrackState.Separating,
            JobStage.Handoff => TrackState.Staging,
            JobStage.Tagging => TrackState.Tagging,
            _ => TrackState.Queued,
        };

    /// <summary>
    /// What the track is when crateside has nothing in flight for it — read off the ledger and
    /// what is on disk rather than off anything crateside recorded running.
    /// </summary>
    private TrackState AtRest =>
        Samples switch
        {
            // A sample that has already moved is not waiting to be tagged: crateside never writes
            // tags in Mashup Samples, and the move covers tagging along with everything else.
            { Count: > 0 } when Samples.All(s => s.IsWarped || s.IsKeyTagged) =>
                TrackState.ReadyToWarp,
            { Count: > 0 } => TrackState.Tagging,
            _ => TrackState.Queued,
        };

    private string InLine =>
        GpuPosition is 1
            ? "next on the GPU"
            : $"{GpuPosition.GetValueOrDefault().Ordinalize()} for the GPU";
}

/// <summary>
/// One file of a track that is meant to be kept: tagged, then warped, then moved.
/// </summary>
/// <remarks>
/// A warp guide is not one of these, because it is used up rather than kept. It <em>is</em>
/// tagged — with the key and no type, so it reads sensibly if the user happens to select it in
/// the browser — but the key is a song-level fact the row already holds, so a guide carries no
/// per-file detail of its own and the row records only that one is still on disk.
/// </remarks>
public sealed record TrackSample
{
    /// <summary>The StemForge preset, or <c>source</c> for a kept instrumental release.</summary>
    public required string Label { get; init; }

    /// <summary>Whether Ableton has a key on the file.</summary>
    public bool IsKeyTagged { get; init; }

    /// <summary>
    /// Whether this sample has reached <c>Mashup Samples</c>, which is what says it is finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from <em>where the file is</em>, never from an <c>.asd</c> sidecar. Ableton writes one
    /// of those merely for having looked at a file — a single click to preview it is enough — so
    /// its presence marks unfinished work as finished, which is why
    /// <c>docs/adr/0002</c> deleted that reader rather than weakening it.
    /// </para>
    /// <para>
    /// <strong>Per sample, not per track</strong>: the user empties staging progressively, so a
    /// track can have its vocal warped and moved while its instrumental is still waiting.
    /// </para>
    /// </remarks>
    public bool IsWarped { get; init; }
}
