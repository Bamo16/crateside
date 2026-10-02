using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Plans;
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
    /// Staged files not yet carrying the name the user stated — zero unless a rename was
    /// interrupted part way.
    /// </summary>
    public int AwaitingRename { get; init; }

    /// <summary>What renaming would touch, for the button that offers it.</summary>
    public string RenameScope =>
        FileCount is 0 ? "at staging" : $"{"file".ToQuantity(FileCount)} + folder info";

    /// <summary>The sentence beside the button, saying what <see cref="RenameScope"/> means.</summary>
    public string RenameDetail =>
        FileCount is 0
            ? "Nothing of this track is in Sample Staging yet, so the name is recorded now and the stems are made with it."
            : $"Renames the files in {Folders} and their entries in Ableton's tag store, so the tags follow the name.";

    /// <summary>This track's files on disk, warp guide included.</summary>
    private int FileCount => Samples.Count + (HasWarpGuide ? 1 : 0);

    private string Folders =>
        (
            Samples.Any(sample => !sample.IsInLibrary) || HasWarpGuide,
            Samples.Any(sample => sample.IsInLibrary)
        ) switch
        {
            (true, true) => "Sample Staging and Mashup Samples",
            (false, true) => "Mashup Samples",
            _ => "Sample Staging",
        };

    /// <summary>The current verdict, or null when the ledger has never seen this track.</summary>
    public LedgerRun? Verdict { get; init; }

    /// <summary>
    /// The judged song this unjudged track's name looks like; judging waits until the user says
    /// whether it is (#41).
    /// </summary>
    public LedgerRun? Lookalike { get; init; }

    /// <summary>
    /// What the genre store knows this song by: the ledger's identity, or the upload for a song it
    /// never judged (ADR 0030).
    /// </summary>
    public string Song => Verdict?.Song ?? VideoId;

    /// <summary>The song's genre as settled or suggested, or null when nothing has asked yet.</summary>
    public GenreRecord? Genre { get; init; }

    /// <summary>How far the song reaches, measured or settled, or null when nothing has asked yet.</summary>
    public FamiliarityRecord? Familiarity { get; init; }

    public bool HasTagsToAnswer =>
        this
            is { Genre: { IsSettled: false, LookedUpAt: not null } }
                or { Familiarity: { Written: null, LookedUpAt: not null } };

    /// <summary>
    /// The ledger's artist and title, else the row's own name split at its dash. Null while the row
    /// has only the upload's title, whose artist is a guess.
    /// </summary>
    public (string Artist, string Title)? ArtistAndTitle =>
        Verdict is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
            ? (artist, title)
        : NameSource is TrackNameSource.Playlist or TrackNameSource.VideoId ? null
        : Title.Split(" - ", 2) is [{ Length: > 0 } a, { Length: > 0 } t] ? (a, t)
        : null;

    /// <summary>The key to tag with, or null when withheld, weak, or unknown.</summary>
    public string? Key => Verdict?.HasTaggableKey is true ? Verdict.Key : null;

    public KeyConfidence Confidence => Verdict?.Confidence ?? KeyConfidence.Unknown;

    /// <summary>
    /// The finished warp's tempo, else keytag's estimate as a starting point for warping, which a
    /// verdict too weak to tag with still measured (ADR 0038).
    /// </summary>
    public double? Bpm => Tempo?.Bpm ?? Verdict?.Bpm;

    /// <summary>Where <see cref="Bpm"/> came from, and whether the tempo holds.</summary>
    public string? BpmNote =>
        Tempo switch
        {
            null when TempoDrifts => Verdict?.Bpm is null
                ? "the tempo drifts: expect a hard warp"
                : "keytag's estimate · the tempo drifts: expect a hard warp",
            null => Verdict?.Bpm is null ? null : "keytag's estimate",
            { Shape: TempoShape.Drifting, Low: var low, High: var high } =>
                $"from your warp: drifting, {low:N0}–{high:N0}",
            { Shape: TempoShape.MostlySteady } =>
                "from your warp: mostly steady, changing in places",
            _ => "from your warp",
        };

    /// <summary>The tempo of a finished warp, the instrumental's where there is one.</summary>
    public WarpTempo? Tempo =>
        Samples
            .Where(sample => sample.Tempo is not null)
            .OrderByDescending(sample =>
                sample.Label.StartsWith("Instrumental", StringComparison.OrdinalIgnoreCase)
            )
            .Select(sample => sample.Tempo)
            .FirstOrDefault();

    /// <summary>beatdetect's reading of the instrumental, once the worker has read it.</summary>
    public BeatReading? Beats =>
        Samples.Select(sample => sample.Beats).FirstOrDefault(reading => reading is not null);

    /// <summary>Not warped yet, and beatdetect reads the tempo drifting (#75).</summary>
    public bool TempoDrifts => this is { Tempo: null, Beats.Drifts: true };

    /// <summary>The stage crateside currently believes is working this track, if any.</summary>
    public JobStage? RunningStage { get; init; }

    /// <summary>When <see cref="RunningStage"/> started, for the header's elapsed clock.</summary>
    public DateTimeOffset? RunningSince { get; init; }

    /// <summary>The stage that ran out of strikes, if one has.</summary>
    public JobStage? GaveUpAt { get; init; }

    /// <summary>How many times <see cref="GaveUpAt"/> failed before it stopped trying.</summary>
    public int GaveUpStrikes { get; init; }

    /// <summary>Why no retry can help, such as <c>upload unavailable</c>; null when the strikes simply ran out.</summary>
    public string? GaveUpFor { get; init; }

    public StageFailure? Retrying { get; init; }

    /// <summary>Samples of this song crateside sent to the recycle bin, most recent first.</summary>
    public IReadOnlyList<Deletion> Deletions { get; init; } = [];

    /// <summary>
    /// The row exists only because the library has files for it: crateside never queued it and it
    /// is not in staging.
    /// </summary>
    public bool IsLibraryOnly { get; init; }

    /// <summary>Which files the user wants made for this track.</summary>
    public StemPlan Plan { get; init; } = StemPlan.Default;

    /// <summary>The user pasted this track in rather than adding it to the playlist (ADR 0010).</summary>
    public bool IsAddedByUrl { get; init; }

    /// <summary>Queued from the Library to be judged one per idle Claude window (ADR 0035).</summary>
    public bool IsBackfill { get; init; }

    /// <summary>Whether Remove can take this track out of crateside (ADR 0027).</summary>
    public bool CanForget =>
        this is { PlaylistItemId: null, RunningStage: null }
        && !Samples.Any(sample => sample.IsInLibrary);

    /// <summary>
    /// Why crateside stopped and handed this track back, or null when it has not.
    /// </summary>
    public ParkReason? ParkedFor { get; init; }

    /// <summary>
    /// How the held download turned out, or null when there is none; a finding only when
    /// <see cref="Acquisition.Stops"/> says so, not by being here.
    /// </summary>
    public Acquisition? Acquisition { get; init; }

    /// <summary>Its place in the line for the GPU, counting from one. Null when it is not in it.</summary>
    public int? GpuPosition { get; init; }

    /// <summary>
    /// The files meant to be kept, warp guides excluded — usually stems, but for an instrumental
    /// release the source track itself (ADR 0004).
    /// </summary>
    public IReadOnlyList<TrackSample> Samples { get; init; } = [];

    /// <summary>
    /// A warp guide is still on disk, so warping is outstanding; its absence claims nothing (ADR 0004).
    /// </summary>
    public bool HasWarpGuide { get; init; }

    /// <summary>
    /// The folder holding this track's files, staging first, or null when none are on disk yet.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    /// When this track's files arrived in <c>Sample Staging</c>, read off the files, or null once
    /// none are left (ADR 0008).
    /// </summary>
    public DateTimeOffset? StagedSince { get; init; }

    /// <summary>
    /// Where the track has got to — outstanding work outranks done, since a swapped track's old
    /// samples can all be in the library while its replacement is still queued.
    /// </summary>
    public TrackState State =>
        this switch
        {
            { RunningStage: { } stage } => InFlight(stage),
            { GpuPosition: not null } => TrackState.JudgedInLine,
            // Ahead of done and ready-to-warp, which are both claims that nothing is missing.
            { IsShort: true, ParkedFor: null, GaveUpAt: null } => TrackState.Short,
            { IsDone: true } => TrackState.Done,
            { GaveUpAt: not null } => TrackState.GaveUp,
            { ParkedFor: not null } => TrackState.Parked,
            _ => AtRest,
        };

    /// <summary>What this track already has on disk, which is what narrows the recipe.</summary>
    public StemInventory Inventory { get; init; } = StemInventory.Empty;

    /// <summary>What separation will ask for, once it gets to this track.</summary>
    public SeparationRecipe Recipe => SeparationRecipe.For(Plan, Inventory);

    /// <summary>What the plan asks for that is not on disk (ADR 0027).</summary>
    public StemPlan Shortfall => Inventory.Shortfall(Plan);

    /// <summary>
    /// Cut once and missing presets its plan asks for, which only a re-cut the user asks for makes.
    /// </summary>
    public bool IsShort =>
        this is { IsLibraryOnly: false, Inventory.IsClassified: true, Samples.Count: > 0 }
        && Shortfall.Presets.Count > 0;

    /// <summary>
    /// Whether the plan can still change, which is until separation starts — read off what has
    /// happened, not <see cref="State"/>, since only <see cref="ParkReason.NoKey"/> follows the GPU.
    /// </summary>
    public bool CanChangePlan =>
        State is not (TrackState.Separating or TrackState.Staging or TrackState.Tagging)
        && this is { ParkedFor: not ParkReason.NoKey, HasWarpGuide: false, Samples.Count: 0 };

    /// <summary>
    /// A change to this track's files is already being made, so the plan is not open to another.
    /// </summary>
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
            IsLibraryOnly = IsLibraryOnly,
            HasVerdict = Verdict is not null,
        };

    /// <summary>
    /// A finding to flag beside the song name — for now an Ableton tag that no longer matches the
    /// ledger — or null when there is nothing wrong.
    /// </summary>
    public string? AuditNote { get; init; }

    /// <summary>
    /// Headed for judging with nothing in its way: unjudged, and queued, downloading or detecting —
    /// the same count whichever of those it is in.
    /// </summary>
    public bool IsAwaitingJudgement =>
        this is { Verdict: null, Lookalike: null, IsLibraryOnly: false, IsBackfill: false }
        && State is TrackState.Queued or TrackState.Downloading or TrackState.Detecting;

    /// <summary>What the state chip reads.</summary>
    public string ChipLabel =>
        State switch
        {
            TrackState.Queued when Lookalike is not null => "same song?",
            TrackState.Queued => "queued",
            TrackState.Downloading => "downloading",
            TrackState.Detecting => "detecting key",
            TrackState.Judging => "judging",
            TrackState.JudgedInLine => "judged · in line",
            TrackState.Separating => "separating",
            TrackState.Staging => "staging",
            TrackState.Tagging => "tagging",
            TrackState.ReadyToWarp => "ready to warp",
            TrackState.ReadyToMove => "ready to move",
            TrackState.Short => $"short · {"file".ToQuantity(Shortfall.Presets.Count)} missing",
            TrackState.Parked => ParkedFor is ParkReason.PremiumShortfall
                ? "parked · poor audio"
                : "parked · no key",
            TrackState.GaveUp => GaveUpFor is { } why
                ? $"gave up · {why}"
                : $"gave up · {GaveUpAt?.ToString().ToLowerInvariant()} ×{GaveUpStrikes}",
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
            TrackState.ReadyToWarp or TrackState.ReadyToMove => TrackChipStyle.AmberFilled,
            TrackState.Parked or TrackState.Short => TrackChipStyle.RedOutline,
            TrackState.Queued when Lookalike is not null => TrackChipStyle.RedOutline,
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
            TrackState.Queued when Lookalike is { } like =>
                $"is this {like.DisplayName}? judging waits for your answer",
            TrackState.Queued when IsBackfill => "waiting for an idle Claude window",
            TrackState.Downloading or TrackState.Detecting when IsBackfill =>
                "then judged in an idle Claude window",
            TrackState.Downloading => "then judging",
            TrackState.Detecting => "then judging",
            TrackState.Judging => "verdict in about a minute",
            TrackState.JudgedInLine => InLine,
            TrackState.Separating => "then staging",
            TrackState.Staging => "moving stems into Sample Staging",
            TrackState.Tagging => "writing Ableton key tags",
            TrackState.ReadyToWarp => Warped is 0
                ? "your turn — warp, then move to Mashup Samples"
                : $"your turn — {Warped} of {Staged} warped, then move to Mashup Samples",
            TrackState.ReadyToMove => HasWarpGuide
                ? "warped — delete the warp guide, then move to Mashup Samples"
                : "warped — move to Mashup Samples",
            TrackState.Parked => ParkedFor is ParkReason.PremiumShortfall
                ? "check for a better upload, or use this one anyway"
                : "needs a better verdict — nothing automatic will move it",
            TrackState.GaveUp => "retry, or take it out of the playlist",
            TrackState.Short => "cut what is missing, or drop it from the plan",
            TrackState.Done => "in Mashup Samples",
            _ => "downloading next",
        };

    /// <summary>
    /// The expanded row's sentence: what the verdict was, whether Ableton still agrees with it,
    /// and how long the track has been sitting in staging.
    /// </summary>
    public string Detail =>
        string.Join(
            " ",
            new[] { Judgement, Unfetchable, PremiumShortfall, Disagreement, Since }.OfType<string>()
        );

    /// <summary>Said while a stage is owed another attempt; its error is too long to inline.</summary>
    public string? RetryNote =>
        Retrying is { Stage: var stage, Strikes: var strikes }
            ? $"{stage} failed and will be tried again ({strikes} of {"strike".ToQuantity(StageEligibility.MaxStrikesFor(stage))})."
            : null;

    /// <summary>Said only while the premium shortfall is still the user's to resolve.</summary>
    private string? PremiumShortfall =>
        ParkedFor is ParkReason.PremiumShortfall ? Acquisition?.Advisory : null;

    private string Judgement =>
        this switch
        {
            { Verdict: null } => "Never judged.",
            { Key: { } key } => $"Judged {key}, {Spoken(Confidence)} confidence.",
            _ =>
                $"Judged, and the verdict came back {Spoken(Confidence)} — no key worth tagging with.",
        };

    /// <summary>
    /// Said only where a conflict was found; null does not mean the tags are correct (ADR 0003).
    /// </summary>
    private string? Disagreement =>
        AuditNote is null ? null : "Ableton's key on these files is not the current verdict.";

    private string? Unfetchable =>
        GaveUpFor is { } why
            ? $"YouTube will not serve this upload ({why}), so retrying will not help."
                + (
                    IsLibraryOnly
                        ? " It cannot be judged from this upload."
                        : " Swap source can point it at another upload."
                )
            : null;

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
            { Count: > 0 } when !Samples.All(s => s.IsInLibrary || s.IsKeyTagged) =>
                TrackState.Tagging,
            { Count: > 0 } when Samples.All(s => s.IsInLibrary || s.HasDefaultClip) =>
                TrackState.ReadyToMove,
            { Count: > 0 } => TrackState.ReadyToWarp,
            _ => TrackState.Queued,
        };

    private int Staged => Samples.Count(sample => !sample.IsInLibrary);

    private int Warped =>
        Samples.Count(sample => sample is { IsInLibrary: false, HasDefaultClip: true });

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
    /// Never parse it out of <see cref="Label"/>, which can read like one when it is not.
    /// </summary>
    public string? PresetId { get; init; }

    /// <summary>
    /// The key Ableton holds on this file — from the tag store while staged, from its index once
    /// in the library (ADR 0013) — or null when it holds none.
    /// </summary>
    public string? AbletonKey { get; init; }

    /// <summary>Whether Ableton has a key on the file.</summary>
    public bool IsKeyTagged => AbletonKey is not null;

    /// <summary>Whether it was downloaded as premium audio. Null when the file does not say.</summary>
    public bool? IsPremium { get; init; }

    /// <summary>Whether this sample has reached <c>Mashup Samples</c>, which is what says it is done.</summary>
    public bool IsInLibrary { get; init; }

    /// <summary>
    /// Its default clip is saved, which only a finished warp gets. Read for staged samples only
    /// (ADR 0037).
    /// </summary>
    public bool HasDefaultClip { get; init; }

    /// <summary>The sibling whose warp crateside copied onto it, until the user saves over it (ADR 0043).</summary>
    public string? WarpBorrowedFrom { get; init; }

    /// <summary>The tempo its warp holds, once its default clip is saved (ADR 0038).</summary>
    public WarpTempo? Tempo { get; init; }

    /// <summary>beatdetect's reading, kept for instrumentals only.</summary>
    public BeatReading? Beats { get; init; }

    /// <summary>
    /// The upload this file was cut from. A song that has been source-swapped holds samples from
    /// more than one, and their filenames need not differ (ADR 0014).
    /// </summary>
    public string? SourceVideoId { get; init; }
}
