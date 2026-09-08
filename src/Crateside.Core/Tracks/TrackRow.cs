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

    /// <summary>
    /// The playlist entry, which is what removing the track targets. Null once it has none.
    /// </summary>
    /// <remarks>
    /// A track leaves the playlist as soon as it is fully processed, which is long before the user
    /// has finished with it — its stems sit in <c>Sample Staging</c> for weeks afterwards. So a row
    /// without a playlist entry is the normal end state, not a broken one, and the only thing it
    /// costs is that there is nothing left to remove.
    /// </remarks>
    public string? PlaylistItemId { get; init; }

    /// <summary>What the track is called: the user's name for it, or the derived one.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// What it would be called if the user had not named it — and null when they have not.
    /// </summary>
    public string? DerivedTitle { get; init; }

    /// <summary>The user has given this track a name of their own.</summary>
    public bool IsRenamed => DerivedTitle is not null;

    /// <summary>Which source the derived name came from.</summary>
    public TrackNameSource NameSource { get; init; }

    /// <summary>
    /// Staged files a rename did not reach — zero unless one was interrupted part way.
    /// </summary>
    /// <remarks>
    /// Counted against a name the user stated, never a derived one, which routinely differs from
    /// what StemForge wrote.
    /// </remarks>
    public int AwaitingRename { get; init; }

    /// <summary>
    /// Whether crateside can still put a name on this track's files.
    /// </summary>
    /// <remarks>A single file in <c>Mashup Samples</c> locks the whole row (ADR 0003).</remarks>
    public bool CanRename => !Samples.Any(sample => sample.IsInLibrary);

    /// <summary>What renaming would touch, for the button that offers it.</summary>
    public string RenameScope =>
        StagedFileCount is 0 ? "at staging" : $"{"file".ToQuantity(StagedFileCount)} + folder info";

    /// <summary>The sentence beside the button, saying what <see cref="RenameScope"/> means.</summary>
    public string RenameDetail =>
        StagedFileCount is 0
            ? "Nothing of this track is in Sample Staging yet, so the name is recorded now and the stems are made with it."
            : "Renames the files in Sample Staging and their entry in Ableton's tag store, so the tags follow the name.";

    /// <summary>This track's files still in <c>Sample Staging</c>, warp guide included.</summary>
    private int StagedFileCount =>
        Samples.Count(sample => !sample.IsInLibrary) + (HasWarpGuide ? 1 : 0);

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

    /// <summary>When <see cref="RunningStage"/> started, for the header's elapsed clock.</summary>
    public DateTimeOffset? RunningSince { get; init; }

    /// <summary>The stage that ran out of strikes, if one has.</summary>
    /// <remarks>
    /// Reduced in one place so that <em>every</em> stage is checked. The queue page assembled this
    /// itself and left tagging out, so a track that gave up there said nothing at all.
    /// </remarks>
    public JobStage? GaveUpAt { get; init; }

    /// <summary>How many times <see cref="GaveUpAt"/> failed before it stopped trying.</summary>
    public int GaveUpStrikes { get; init; }

    /// <summary>
    /// The row exists only because the library has files for it: crateside never queued it and it
    /// is not in staging.
    /// </summary>
    /// <remarks>
    /// Tracks keytag judged before crateside existed, and tracks crateside finished with long
    /// enough ago that the playlist entry is gone. They belong on the page — they are the only way
    /// to reach an old track at all — but not in the working list, which is what
    /// <see cref="TrackFilter.All"/> is for.
    /// </remarks>
    public bool IsLibraryOnly { get; init; }

    /// <summary>The user has told crateside to leave this track alone.</summary>
    public bool IsHeld { get; init; }

    /// <summary>
    /// The user has said this release has no vocals, which is what the recipe is derived from.
    /// </summary>
    /// <remarks>
    /// Carried so the expanded row can draw the plan and offer the one control over it. False
    /// means <em>not stated</em> rather than "it has vocals" — the two behave alike, because
    /// crateside's policy for not-stated is to expect the usual pair (ADR 0004).
    /// </remarks>
    public bool IsInstrumentalRelease { get; init; }

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
    /// The folder holding this track's files, or null when it has none on disk yet.
    /// </summary>
    /// <remarks>
    /// Staging wins over wherever else the files have got to, because staging is where the work
    /// that is left happens: the button exists to open the folder the user is about to drag out
    /// of.
    /// </remarks>
    public string? Folder { get; init; }

    /// <summary>
    /// When this track's files arrived in <c>Sample Staging</c>, or null once none are left.
    /// </summary>
    /// <remarks>
    /// A track rests here for weeks, so how long is the one thing about it worth saying. Read from
    /// the file rather than recorded by crateside, which is what lets it answer for tracks that
    /// were already staged before anything thought to write it down (ADR 0008).
    /// </remarks>
    public DateTimeOffset? StagedSince { get; init; }

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
    /// What separation will ask for, once it gets to this track.
    /// </summary>
    /// <remarks>
    /// A plan and never a fact, which is why nothing records it: it is derived from
    /// <see cref="IsInstrumentalRelease"/> the same way the work order derives it, so the row
    /// draws what will actually run rather than a second opinion about it (ADR 0004).
    /// </remarks>
    public SeparationRecipe Recipe => SeparationRecipe.For(IsInstrumentalRelease);

    /// <summary>
    /// Whether the separation plan can still be changed, which is until separation starts.
    /// </summary>
    /// <remarks>
    /// Read off what has already happened rather than off <see cref="State"/>, because a hold
    /// outranks nearly everything below it: a track separated, parked and then held still reads
    /// as <c>held</c>, and a plan stops being a plan the moment its stems exist.
    /// </remarks>
    public bool CanChangePlan =>
        State is not (TrackState.Separating or TrackState.Staging or TrackState.Tagging)
        && this is { IsParkedForKey: false, HasWarpGuide: false, Samples.Count: 0 };

    /// <summary>
    /// A finding worth flagging beside the song name, or null when there is nothing wrong.
    /// </summary>
    /// <remarks>
    /// Currently only the one the staging board can see on its own: Ableton holding a key that is
    /// not the ledger's current verdict, which is what a supersede leaves behind. The tag is what
    /// pairing actually uses, so a stale one silently misroutes mashups.
    /// </remarks>
    public string? AuditNote { get; init; }

    /// <summary>
    /// What the state chip reads.
    /// </summary>
    /// <remarks>
    /// Here rather than in the page, beside <see cref="Next"/> and derived from the same
    /// <see cref="State"/>, because those two are the copy table and it only stays honest while
    /// they cannot be edited apart.
    /// </remarks>
    public string ChipLabel =>
        State switch
        {
            TrackState.Queued => "queued",
            TrackState.Detecting => "detecting key",
            TrackState.Judging => "judging",
            TrackState.JudgedInLine => "judged · in line",
            TrackState.Separating => "separating",
            TrackState.Staging => "staging",
            TrackState.Tagging => "tagging",
            TrackState.ReadyToWarp => "ready to warp",
            TrackState.Parked => "parked · no key",
            TrackState.GaveUp =>
                $"gave up · {GaveUpAt?.ToString().ToLowerInvariant()} ×{GaveUpStrikes}",
            TrackState.Held => "held",
            TrackState.Done => "done",
            _ => string.Empty,
        };

    /// <summary>
    /// How the chip is drawn.
    /// </summary>
    /// <remarks>
    /// Teal is the machine working, amber is the user's turn, red needs attention, green is done
    /// and behind them. Nothing else is coloured — a palette that means something stops meaning it
    /// the moment a fifth colour appears for decoration.
    /// </remarks>
    public TrackChipStyle ChipStyle =>
        State switch
        {
            TrackState.Detecting
            or TrackState.Judging
            or TrackState.Separating
            or TrackState.Staging
            or TrackState.Tagging => TrackChipStyle.TealFilled,
            TrackState.JudgedInLine => TrackChipStyle.TealOutline,
            TrackState.ReadyToWarp => TrackChipStyle.AmberFilled,
            TrackState.Parked => TrackChipStyle.RedOutline,
            TrackState.GaveUp => TrackChipStyle.RedFilled,
            TrackState.Held => TrackChipStyle.DashedOutline,
            TrackState.Done => TrackChipStyle.GreenOutline,
            _ => TrackChipStyle.NeutralOutline,
        };

    /// <summary>
    /// Whether the chip carries a dot — the mark of work happening right now.
    /// </summary>
    /// <remarks>
    /// Exactly the filled teal states, which are the ones where a process is running this second.
    /// Waiting in line for the GPU is teal but not filled and takes no dot: nothing is happening
    /// to that track yet.
    /// </remarks>
    public bool ChipHasDot => ChipStyle is TrackChipStyle.TealFilled;

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
    /// The expanded row's sentence: what the verdict was, whether Ableton still agrees with it,
    /// and how long the track has been sitting in staging.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Next"/> and <see cref="ChipLabel"/> for the reason they are here at all:
    /// it is copy derived from the same reading, and copy that can be edited apart from what it
    /// describes drifts away from it.
    /// </remarks>
    public string Detail =>
        string.Join(" ", new[] { Judgement, Disagreement, Since }.OfType<string>());

    private string Judgement =>
        this switch
        {
            { Verdict: null } => "Never judged.",
            { Key: { } key } => $"Judged {key}, {Spoken(Confidence)} confidence.",
            _ =>
                $"Judged, and the verdict came back {Spoken(Confidence)} — no key worth tagging with.",
        };

    /// <summary>
    /// Said only where a conflict was actually found.
    /// </summary>
    /// <remarks>
    /// Silence here is not a claim that the tags are right. The Ableton tag store lags Ableton's
    /// own index and keeps entries for files that have left, so the only reading worth repeating
    /// is one that found a disagreement (ADR 0003).
    /// </remarks>
    private string? Disagreement =>
        AuditNote is null ? null : "Ableton's key on these files is not the current verdict.";

    private string? Since => StagedSince is { } staged ? $"Staged {staged.Humanize()}." : null;

    private static string Spoken(KeyConfidence confidence) =>
        confidence.Humanize(LetterCasing.LowerCase);

    /// <summary>
    /// Every sample has left for <c>Mashup Samples</c>. The move is the signal — see
    /// <see cref="TrackSample.IsInLibrary"/>.
    /// </summary>
    private bool IsDone => Samples is { Count: > 0 } && Samples.All(s => s.IsInLibrary);

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
            { Count: > 0 } when Samples.All(s => s.IsInLibrary || s.IsKeyTagged) =>
                TrackState.ReadyToWarp,
            { Count: > 0 } => TrackState.Tagging,
            _ => TrackState.Queued,
        };

    private string InLine =>
        GpuPosition is 1
            ? "next on the GPU"
            : $"{GpuPosition.GetValueOrDefault().Ordinalize()} for the GPU";
}

/// <summary>Where a track's derived name came from, best first.</summary>
public enum TrackNameSource
{
    /// <summary>The files themselves — what Ableton shows, so the row matches it.</summary>
    Files,

    /// <summary>keytag's verdict, for a track with nothing on disk yet.</summary>
    Ledger,

    /// <summary>The playlist entry's title, which is whatever the uploader typed.</summary>
    Playlist,

    /// <summary>Nothing named it. The video ID is all there is.</summary>
    VideoId,
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

    /// <summary>
    /// The key Ableton holds on this file, or null when it holds none.
    /// </summary>
    /// <remarks>
    /// Trustworthy only while the file is in <c>Sample Staging</c>: that is the one folder
    /// crateside writes tags into and the one place its reading of them is a reading of Ableton
    /// rather than of a stale store (ADR 0003). A sample that has reached <c>Mashup Samples</c>
    /// carries null, which says nobody looked rather than that it is untagged.
    /// </remarks>
    public string? AbletonKey { get; init; }

    /// <summary>Whether Ableton has a key on the file.</summary>
    public bool IsKeyTagged => AbletonKey is not null;

    /// <summary>
    /// Whether this sample has reached <c>Mashup Samples</c> — the finished library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Named for what was measured, not for what it implies.</strong> This is the folder
    /// the file is in and nothing else. crateside infers from that move that the user has warped
    /// it (ADR 0007), and that inference is fine for deciding a track is done — but it is not
    /// something to assert about a particular file on screen, because nobody here watched anyone
    /// warp anything.
    /// </para>
    /// <para>
    /// Read from where the file is, never from an <c>.asd</c> sidecar. Ableton writes one of those
    /// merely for having looked at a file — a single click to preview it is enough — so its
    /// presence marks unfinished work as finished, which is why <c>docs/adr/0002</c> deleted that
    /// reader rather than weakening it.
    /// </para>
    /// <para>
    /// <strong>Per sample, not per track</strong>: the user empties staging progressively, so a
    /// track can have one file moved out while the rest are still waiting.
    /// </para>
    /// </remarks>
    public bool IsInLibrary { get; init; }
}
