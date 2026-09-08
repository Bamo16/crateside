using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Humanizer;

namespace Crateside.Core.Tracks;

/// <summary>
/// One track, with everything known about it rolled up: the row the Tracks page draws.
/// </summary>
public sealed record TrackRow
{
    public required string VideoId { get; init; }

    /// <summary>
    /// The playlist entry, which is what removing the track targets. Null once it has none.
    /// </summary>
    public string? PlaylistItemId { get; init; }

    /// <summary>What the track is called: the user's name for it, or the derived one.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// What it would be called if the user had not named it — and null when they have not.
    /// </summary>
    public string? DerivedTitle { get; init; }

    public bool IsRenamed => DerivedTitle is not null;

    public TrackNameSource NameSource { get; init; }

    /// <summary>
    /// Staged files a rename did not reach — zero unless one was interrupted part way.
    /// </summary>
    /// <remarks>
    /// Counted against the name the user stated, never a derived one, which can differ from what
    /// StemForge wrote.
    /// </remarks>
    public int AwaitingRename { get; init; }

    /// <summary>A single file already in <c>Mashup Samples</c> locks the whole row (ADR 0003).</summary>
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
    /// Populated even when <see cref="Key"/> is null — a verdict too weak to tag with still
    /// measured a tempo.
    /// </remarks>
    public double? Bpm => Verdict?.Bpm;

    /// <summary>The stage crateside currently believes is working this track, if any.</summary>
    public JobStage? RunningStage { get; init; }

    /// <summary>When <see cref="RunningStage"/> started, for the header's elapsed clock.</summary>
    public DateTimeOffset? RunningSince { get; init; }

    /// <summary>The stage that ran out of strikes, if one has.</summary>
    public JobStage? GaveUpAt { get; init; }

    /// <summary>How many times <see cref="GaveUpAt"/> failed before it stopped trying.</summary>
    public int GaveUpStrikes { get; init; }

    /// <summary>
    /// The row exists only because the library has files for it: crateside never queued it and it
    /// is not in staging.
    /// </summary>
    /// <remarks>
    /// Not part of <see cref="TrackFilter.All"/> — reachable only through
    /// <see cref="TrackFilter.Done"/>.
    /// </remarks>
    public bool IsLibraryOnly { get; init; }

    /// <summary>The user has told crateside to leave this track alone.</summary>
    public bool IsHeld { get; init; }

    /// <summary>
    /// The user has said this release has no vocals, which is what the recipe is derived from.
    /// </summary>
    /// <remarks>
    /// False means <em>not stated</em> rather than "it has vocals" — crateside's policy for
    /// not-stated is to expect the usual pair (ADR 0004).
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
    /// rather than kept, not because they are untagged: they carry the key like everything else.
    /// </summary>
    /// <remarks>
    /// For most tracks the vocal and instrumental stems; for an instrumental release, the source
    /// track, which is a sample and no kind of stem at all (ADR 0004).
    /// </remarks>
    public IReadOnlyList<TrackSample> Samples { get; init; } = [];

    /// <summary>A warp guide is still on disk, which says warping is outstanding.</summary>
    /// <remarks>
    /// Its absence is not a claim that warping happened (ADR 0004).
    /// </remarks>
    public bool HasWarpGuide { get; init; }

    /// <summary>
    /// The folder holding this track's files, or null when it has none on disk yet.
    /// </summary>
    /// <remarks>Staging wins over wherever else the files have got to.</remarks>
    public string? Folder { get; init; }

    /// <summary>
    /// When this track's files arrived in <c>Sample Staging</c>, or null once none are left.
    /// </summary>
    /// <remarks>
    /// Read off the file, not recorded by crateside — it can answer for tracks staged before
    /// anything tracked it (ADR 0008).
    /// </remarks>
    public DateTimeOffset? StagedSince { get; init; }

    /// <summary>
    /// Where the track has got to.
    /// </summary>
    /// <remarks>
    /// Resolved in precedence order — done first, then any stage in flight, then held, then given
    /// up.
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

    /// <summary>What separation will ask for, once it gets to this track.</summary>
    public SeparationRecipe Recipe => SeparationRecipe.For(IsInstrumentalRelease);

    /// <summary>
    /// Whether the separation plan can still be changed, which is until separation starts.
    /// </summary>
    /// <remarks>
    /// Read off what has already happened, not off <see cref="State"/> — a held track that was
    /// already separated still reads as held, but its plan is fixed.
    /// </remarks>
    public bool CanChangePlan =>
        State is not (TrackState.Separating or TrackState.Staging or TrackState.Tagging)
        && this is { IsParkedForKey: false, HasWarpGuide: false, Samples.Count: 0 };

    /// <summary>
    /// A finding worth flagging beside the song name, or null when there is nothing wrong.
    /// </summary>
    /// <remarks>
    /// Currently just a stale Ableton tag — one that no longer matches the ledger's verdict, which
    /// is what pairing actually uses.
    /// </remarks>
    public string? AuditNote { get; init; }

    /// <summary>What the state chip reads.</summary>
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

    /// <summary>How the chip is drawn.</summary>
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

    /// <summary>Whether the chip carries a dot — the mark of work happening right now.</summary>
    public bool ChipHasDot => ChipStyle is TrackChipStyle.TealFilled;

    /// <summary>What happens next, in plain English — or what is waiting on the user.</summary>
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

    /// <summary>Said only where a conflict was actually found.</summary>
    /// <remarks>
    /// Null does not mean the tags are correct — the Ableton tag store lags and keeps entries for
    /// files that have left (ADR 0003).
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
            // A moved sample counts as tagged too — crateside never re-tags in Mashup Samples.
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
public sealed record TrackSample
{
    /// <summary>The StemForge preset, or <c>source</c> for a kept instrumental release.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// The key Ableton holds on this file, or null when it holds none.
    /// </summary>
    /// <remarks>
    /// Trustworthy only while the file is in <c>Sample Staging</c> (ADR 0003) — null once the
    /// sample reaches <c>Mashup Samples</c> means nobody looked, not that it's untagged.
    /// </remarks>
    public string? AbletonKey { get; init; }

    /// <summary>Whether Ableton has a key on the file.</summary>
    public bool IsKeyTagged => AbletonKey is not null;

    /// <summary>Whether this sample has reached <c>Mashup Samples</c> — the finished library.</summary>
    /// <remarks>
    /// Says only which folder the file is in; crateside infers warping from that but never watched
    /// it happen. Never read from an <c>.asd</c> sidecar — Ableton writes one just from a preview
    /// click, which would mark unfinished work as done (ADR 0002).
    /// </remarks>
    public bool IsInLibrary { get; init; }
}
