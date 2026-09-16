using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
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

    /// <summary>The upload this track is cut from, when it is not the one <see cref="VideoId"/> names.</summary>
    public string? StatedSourceUrl { get; init; }

    public bool IsSourceSwapped => StatedSourceUrl is { Length: > 0 };

    /// <summary>This track's samples a swap cannot reach, because crateside never writes there.</summary>
    public int SamplesLeftInLibrary => Samples.Count(sample => sample.IsInLibrary);

    /// <summary>Whether an upload is one the user attached rather than one cut from (ADR 0019).</summary>
    /// <remarks>
    /// An added source is kept <em>alongside</em> the stems, so calling it an old upload reads as
    /// though a swap had superseded it — the one thing it is not.
    /// </remarks>
    public bool IsAddedSource(string? videoId) =>
        videoId is { Length: > 0 }
        && Plan.AddedSources.Any(source =>
            source.VideoId.Equals(videoId, StringComparison.Ordinal)
        );

    /// <summary>
    /// Whether this row's samples were cut from more than one upload, which is what makes saying
    /// so on each of them worth the room (ADR 0014).
    /// </summary>
    public bool HasSeveralUploads =>
        Samples.Select(sample => sample.SourceVideoId).Distinct(StringComparer.Ordinal).Count() > 1;

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
    /// Always <see cref="TrackState.Done"/>, so it is outside
    /// <see cref="TrackFilter.Unfinished"/> and reachable through
    /// <see cref="TrackFilter.All"/>.
    /// </remarks>
    public bool IsLibraryOnly { get; init; }

    /// <summary>Which files the user wants made for this track.</summary>
    public StemPlan Plan { get; init; } = StemPlan.Default;

    /// <summary>The user pasted this track in rather than adding it to the playlist (ADR 0010).</summary>
    public bool IsAddedByUrl { get; init; }

    /// <summary>
    /// Whether crateside's own record is all that holds this row, so dropping it removes the
    /// track rather than redrawing it.
    /// </summary>
    /// <remarks>
    /// Needed once the intake outlives the playlist entry (ADR 0021): before that, a track the
    /// playlist no longer held simply had no row, and deleting its files was how it went away.
    /// Now the record keeps it — and a judged track with its files deleted reads as one that has
    /// never been cut, which puts it back in the line for the GPU.
    /// </remarks>
    public bool CanForget => PlaylistItemId is null && (IsAddedByUrl || Samples.Count is 0);

    /// <summary>
    /// Why crateside stopped and handed this track back, or null when it has not.
    /// </summary>
    public ParkReason? ParkedFor { get; init; }

    /// <summary>
    /// How the download crateside is holding turned out, or null when it holds none.
    /// </summary>
    /// <remarks>
    /// Present for any track with a working set, shortfall or not — what makes it a finding is
    /// <see cref="Acquisition.Stops"/>, not its being here.
    /// </remarks>
    public Acquisition? Acquisition { get; init; }

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
    /// Outstanding work outranks done. A swapped track's old samples are all in the library while
    /// its replacement is still queued or separating, and reading that as done both hides the job
    /// and drops it from the header's waiting count.
    /// </remarks>
    public TrackState State =>
        this switch
        {
            { RunningStage: { } stage } => InFlight(stage),
            { GpuPosition: not null } => TrackState.JudgedInLine,
            { IsDone: true } => TrackState.Done,
            { GaveUpAt: not null } => TrackState.GaveUp,
            { ParkedFor: not null } => TrackState.Parked,
            _ => AtRest,
        };

    /// <summary>What this track already has on disk, which is what narrows the recipe.</summary>
    public StemInventory Inventory { get; init; } = StemInventory.Empty;

    /// <summary>What separation will ask for, once it gets to this track.</summary>
    public SeparationRecipe Recipe => SeparationRecipe.For(Plan, Inventory);

    /// <summary>
    /// Whether the separation plan can still be changed, which is until separation starts.
    /// </summary>
    /// <remarks>
    /// Read off what has already happened, not off <see cref="State"/>. Only
    /// <see cref="ParkReason.NoKey"/> fixes the plan, because only it means the GPU has already
    /// run — a track parked over its download has had nothing separated and is still open to one.
    /// </remarks>
    public bool CanChangePlan =>
        State is not (TrackState.Separating or TrackState.Staging or TrackState.Tagging)
        && this is { ParkedFor: not ParkReason.NoKey, HasWarpGuide: false, Samples.Count: 0 };

    /// <summary>
    /// A change to this track's files is already being made, so the plan is not open to another.
    /// </summary>
    /// <remarks>
    /// Only once a track has files, because only then is a change a re-cut. A track waiting its
    /// first turn on the GPU is still free to edit: nothing has been made, and the recipe is read
    /// when the separator picks it up rather than when the plan was recorded.
    /// </remarks>
    public bool IsCutInFlight =>
        !CanChangePlan && (RunningStage is not null || GpuPosition is not null);

    /// <summary>What this row knows about itself that attaching another source needs.</summary>
    public TrackRowFacts Facts =>
        new()
        {
            VideoId = VideoId,
            Song = Verdict?.Song,
            Key = Key,
            Plan = Plan,
            Uploads =
            [
                VideoId,
                .. Samples.Select(sample => sample.SourceVideoId).OfType<string>(),
                .. Plan.AddedSources.Select(source => source.VideoId),
            ],
        };

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
            TrackState.Downloading => "downloading",
            TrackState.Detecting => "detecting key",
            TrackState.Judging => "judging",
            TrackState.JudgedInLine => "judged · in line",
            TrackState.Separating => "separating",
            TrackState.Staging => "staging",
            TrackState.Tagging => "tagging",
            TrackState.ReadyToWarp => "ready to warp",
            TrackState.Parked => ParkedFor is ParkReason.PremiumShortfall
                ? "parked · poor audio"
                : "parked · no key",
            TrackState.GaveUp =>
                $"gave up · {GaveUpAt?.ToString().ToLowerInvariant()} ×{GaveUpStrikes}",
            TrackState.Done => "done",
            _ => string.Empty,
        };

    /// <summary>How the chip is drawn.</summary>
    public TrackChipStyle ChipStyle =>
        State switch
        {
            TrackState.Downloading
            or TrackState.Detecting
            or TrackState.Judging
            or TrackState.Separating
            or TrackState.Staging
            or TrackState.Tagging => TrackChipStyle.TealFilled,
            TrackState.JudgedInLine => TrackChipStyle.TealOutline,
            TrackState.ReadyToWarp => TrackChipStyle.AmberFilled,
            TrackState.Parked => TrackChipStyle.RedOutline,
            TrackState.GaveUp => TrackChipStyle.RedFilled,
            TrackState.Done => TrackChipStyle.GreenOutline,
            _ => TrackChipStyle.NeutralOutline,
        };

    /// <summary>Whether the chip carries a dot — the mark of work happening right now.</summary>
    public bool ChipHasDot => ChipStyle is TrackChipStyle.TealFilled;

    /// <summary>What happens next, in plain English — or what is waiting on the user.</summary>
    public string Next =>
        State switch
        {
            TrackState.Downloading => "then judging",
            TrackState.Detecting => "then judging",
            TrackState.Judging => "verdict in about a minute",
            TrackState.JudgedInLine => InLine,
            TrackState.Separating => "then staging",
            TrackState.Staging => "moving stems into Sample Staging",
            TrackState.Tagging => "writing Ableton key tags",
            TrackState.ReadyToWarp => "your turn — warp, then move to Mashup Samples",
            TrackState.Parked => ParkedFor is ParkReason.PremiumShortfall
                ? "check for a better upload, or use this one anyway"
                : "needs a better verdict — nothing automatic will move it",
            TrackState.GaveUp => "retry, or take it out of the playlist",
            TrackState.Done => "in Mashup Samples",
            _ => "downloading next",
        };

    /// <summary>
    /// The expanded row's sentence: what the verdict was, whether Ableton still agrees with it,
    /// and how long the track has been sitting in staging.
    /// </summary>
    public string Detail =>
        string.Join(" ", new[] { Judgement, Shortfall, Disagreement, Since }.OfType<string>());

    /// <summary>Said only while the shortfall is still the user's to resolve.</summary>
    private string? Shortfall =>
        ParkedFor is ParkReason.PremiumShortfall ? Acquisition?.Advisory : null;

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
            JobStage.Download => TrackState.Downloading,
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

    /// <remarks>
    /// A plan whose outstanding half is only the source owes a copy and no passes, so saying
    /// anything about the GPU would be a lie about a track that never reaches it.
    /// </remarks>
    private string InLine =>
        Recipe.Passes is 0 ? "copying the source — no GPU needed"
        : GpuPosition is 1 ? "next on the GPU"
        : $"{GpuPosition.GetValueOrDefault().Ordinalize()} for the GPU";
}

/// <summary>Where a track's derived name came from, best first.</summary>
public enum TrackNameSource
{
    /// <summary>The files themselves — what Ableton shows, so the row matches it.</summary>
    Files,

    /// <summary>keytag's verdict, for a track with nothing on disk yet.</summary>
    Ledger,

    /// <summary>
    /// What StemForge says it will call the files, for a track nothing has run for yet — so the
    /// user can decide about a rename before the GPU does six minutes of work (ADR 0014).
    /// </summary>
    Resolved,

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
    /// <summary>What the separator called this file — <c>Vocal - Full</c> — or <c>source</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Whether it is a stem, the source track kept whole, or neither any more.</summary>
    public StemKind Kind { get; init; }

    /// <summary>
    /// The preset the plan would ask for to make this file again, or null when it cannot ask.
    /// </summary>
    /// <remarks>
    /// Not read off <see cref="Label"/>: 68 files in this library carry no preset in their
    /// provenance and are named only by the separator's display name, and five kept sources have
    /// a parenthesised title that reads like one.
    /// </remarks>
    public string? PresetId { get; init; }

    /// <summary>
    /// The key Ableton holds on this file, or null when it holds none.
    /// </summary>
    /// <remarks>
    /// Read from the tag store while the file is staged and from Ableton's index once it reaches
    /// <c>Mashup Samples</c> (ADR 0013), so a null now means untagged in both places. It used to
    /// mean nobody had looked, because the staging board reads only its own root.
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

    /// <summary>
    /// The upload this file was cut from. A song that has been source-swapped holds samples from
    /// more than one, and their filenames need not differ (ADR 0014).
    /// </summary>
    public string? SourceVideoId { get; init; }
}
