using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ledger;

/// <summary>
/// The full evidence behind one verdict — conclusion, sources and every detector's report — read
/// for one song on demand, never with the whole ledger.
/// </summary>
public sealed record RunEvidence
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Song { get; init; }

    public string? Artist { get; init; }
    public string? Title { get; init; }

    /// <summary>What to call this song on screen — see <see cref="LedgerRun.DisplayName"/>.</summary>
    public string DisplayName =>
        this is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
            ? $"{artist} - {title}"
            : Song;

    /// <summary>The verdict's key, so a source's own answer can be compared against it.</summary>
    public string? Key => Centre?.Collapsed;

    public TonalCentre? Centre { get; init; }

    public DiatonicMode? Mode => Centre?.Mode;

    public bool Mixed { get; init; }

    public IReadOnlyList<Section> Sections { get; init; } = [];

    /// <summary>The stretches detected on their own, where the evidence is not the whole track's.</summary>
    public IReadOnlyList<RunWindow> Windows { get; init; } = [];

    /// <summary>The user's note to the judge, sent with the windows.</summary>
    public string? Direction { get; init; }

    /// <summary>What the detectors read in each window, in the order the windows were detected.</summary>
    public IReadOnlyList<WindowReading> Readings { get; init; } = [];

    /// <summary>
    /// The argument that the tonic is not its relative. Null on older runs, whose prose is in
    /// <see cref="Reasoning"/>.
    /// </summary>
    public string? TonicCheck { get; init; }

    /// <summary>The confidence rationale, and anything else the agent thought worth saying.</summary>
    public string? Reasoning { get; init; }

    /// <summary>The keys the judge weighed besides the verdict's. Empty on runs judged before ADR 0064.</summary>
    public IReadOnlyList<RunCandidate> Candidates { get; init; } = [];

    /// <summary>The reasoning of the first pass, which asked for windows; null on older runs, whose windows carry copies of it.</summary>
    public string? WithheldReasoning { get; init; }

    /// <summary>The first pass's reasoning once: kept on the run, or the copy every judge-asked window carried before ADR 0064.</summary>
    public string? FirstPass => WithheldReasoning ?? SharedWhy;

    /// <summary>What the judge wanted <paramref name="window"/> to settle; null for yours, for one that never said, and for the first pass copied into each window of an older run.</summary>
    public string? WhyOf(RunWindow window) =>
        window is { AskedBy: not WindowAsker.User, Why: { Length: > 0 } why } && SharedWhy is null
            ? why
            : null;

    private string? SharedWhy =>
        WithheldReasoning is null
        && Windows
            .Where(window => window.AskedBy is not WindowAsker.User)
            .Select(window => window.Why)
            .ToList()
            is [_, _, ..] whys
        && whys.Distinct().ToList() is [{ Length: > 0 } why]
            ? why
            : null;

    /// <summary>
    /// Which file was analysed and what kind it was. Worth showing: a verdict reached on an
    /// instrumental is weaker, because the vocal anchors the tonic.
    /// </summary>
    public string? InputKind { get; init; }

    /// <summary>
    /// External sources consulted. Often empty — not a defect, many releases have no human
    /// coverage at all.
    /// </summary>
    public IReadOnlyList<EvidenceSource> Sources { get; init; } = [];

    public IReadOnlyList<AlgorithmResult> Algorithms { get; init; } = [];

    /// <summary>Set when the readings in <see cref="Algorithms"/> were taken after the verdict.</summary>
    public LateReading? LateReading { get; init; }

    /// <summary>
    /// Which detectors agreed. Weighted, not counted: two of the same method family agreeing
    /// is weak, because they share their failure modes.
    /// </summary>
    public IReadOnlyList<AgreementPair> Agreement { get; init; } = [];

    /// <summary>
    /// Which of madmom and edma named keydetect's key; null on a run detected before the port
    /// (#113), or when keydetect read nothing.
    /// </summary>
    public IReadOnlyList<string>? Backers { get; init; }

    /// <summary>The committed chord progression — the cheap check for a relative-key swap.</summary>
    public IReadOnlyList<string> Chords { get; init; } = [];

    /// <summary>Every time the running key estimate changed. Early instability is often just a sparse intro.</summary>
    public IReadOnlyList<KeyEvent> KeyEvents { get; init; } = [];

    /// <summary>The old BPM estimate, a warp starting point.</summary>
    public double? Bpm { get; init; }

    internal static RunEvidence From(RunRecord run)
    {
        var machine = run.Evidence is { } json
            ? JsonSerializer.Deserialize(json, RunEvidenceJsonContext.Default.MachineDto)
            : null;

        return new()
        {
            Timestamp = run.RecordedAt,
            Song = run.Song,
            Artist = run.Artist,
            Title = run.Title,
            Centre = run.Centre,
            Mixed = run.Mixed,
            Sections = run.Sections,
            Windows = run.Windows,
            Direction = run.Direction,
            Readings = WindowReading.Of(run, machine?.Windows ?? []),
            TonicCheck = run.TonicCheck,
            Reasoning = run.Reasoning,
            Candidates = run.Candidates,
            WithheldReasoning = run.WithheldReasoning,
            InputKind = run.InputKind,
            Sources =
            [
                .. run.Sources.Select(source =>
                {
                    var coverage = ModeText.Coverage(
                        source.Sections,
                        run.Sections,
                        run.Centre,
                        source.StatesKey
                    );

                    return new EvidenceSource
                    {
                        Url = source.Url,
                        Via = source.Via,
                        States = source.States,
                        StatesKey = source.StatesKey,
                        StatesMode = source.StatesMode?.ToString().ToLowerInvariant(),
                        RecordedKind = EvidenceSource.StoredName(source.Kind),
                        Note = source.Note,
                        Sections = source.Sections,
                        Covers = coverage.Covers,
                        Relation = coverage.Relation,
                        VerdictKey = coverage.JudgedAgainst,
                    };
                }),
            ],
            Algorithms = AlgorithmResult.FromDto(machine?.Algorithms),
            LateReading = machine is { ReadAfterVerdict: { } late }
                ? new LateReading(late.At, machine.Versions?.GetValueOrDefault("ffmpeg_sha256"))
                : null,
            Agreement =
            [
                .. (machine?.PairwiseAgreement ?? []).Select(pair => new AgreementPair
                {
                    Pair = pair.Pair ?? string.Empty,
                    Status = pair.Status ?? "none",
                    Recommended = pair.Recommended,
                    Note = pair.Note,
                }),
            ],
            Backers = machine?.Backers,
            Chords = machine?.Chords ?? [],
            KeyEvents =
            [
                .. (machine?.KeyEvents ?? []).Select(change => new KeyEvent
                {
                    Seconds = change.T,
                    Key = change.Key ?? string.Empty,
                    R = change.R,
                }),
            ],
            Bpm = run.OldBpm?.Value,
        };
    }
}

/// <summary>One window's detector readings, and the section the verdict gave it, if any.</summary>
public sealed record WindowReading(
    RunWindow Window,
    IReadOnlyList<AlgorithmResult> Algorithms,
    IReadOnlyList<string>? Backers,
    Section? Section
)
{
    /// <summary>The run's windows matched to their reports by span, and to sections by name.</summary>
    internal static IReadOnlyList<WindowReading> Of(
        RunRecord run,
        IReadOnlyList<MachineDto.WindowDto> reports
    ) =>
        [
            .. run.Windows.Select(window =>
            {
                var report = reports
                    .FirstOrDefault(report =>
                        Math.Abs(report.Start - window.Start) < 0.01
                        && Math.Abs(report.End - window.End) < 0.01
                    )
                    ?.Evidence;

                return new WindowReading(
                    window,
                    AlgorithmResult.FromDto(report?.Algorithms),
                    report?.Backers,
                    run.Sections.FirstOrDefault(section =>
                        section.Label.Equals(window.Name, StringComparison.OrdinalIgnoreCase)
                    )
                );
            }),
        ];
}

/// <summary>An external source consulted for a verdict.</summary>
public sealed partial record EvidenceSource
{
    /// <summary>The page, or null when there was none to visit.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// How the source was obtained, when not simply "this link" — <c>"user-provided image"</c> is a
    /// photograph of sheet music.
    /// </summary>
    public string? Via { get; init; }

    /// <summary>Everything the source said about the key, verbatim.</summary>
    public string? States { get; init; }

    /// <summary>
    /// What the source said, in the verdict's own spelling — so it compares. Null when the
    /// source said something that is not a key.
    /// </summary>
    public string? StatesKey { get; init; }

    /// <summary>The diatonic mode the source named, where it named one.</summary>
    public string? StatesMode { get; init; }

    /// <summary>The kind as recorded, from the ledger's closed set.</summary>
    public string? RecordedKind { get; init; }

    /// <summary>
    /// Anything else the agent said about this source: how strong it is, and how a dissent
    /// was handled.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>The labels of the sections the source covered; null when it did not say.</summary>
    public IReadOnlyList<string>? Sections { get; init; }

    /// <summary>What the source covered: <c>whole song</c>, <c>chorus only</c>.</summary>
    public string Covers { get; init; } = "whole song";

    /// <summary>How its key relates to the part it covered, when that is not the whole song.</summary>
    public string? Relation { get; init; }

    /// <summary>The key this source is judged against: the verdict's, or the section it covered.</summary>
    public string? VerdictKey { get; init; }

    /// <summary>
    /// <see cref="Url"/> as something safe to render as a link, or null when it is not one.
    /// </summary>
    public Uri? Link => ParseLink(Url);

    public string? Host => Link?.Host;

    /// <summary>Whether this source agreed with the verdict.</summary>
    public SourceStance Stance =>
        (StatesKey, VerdictKey) switch
        {
            ({ Length: > 0 } stated, { Length: > 0 } verdict) => stated.Equals(
                verdict,
                StringComparison.OrdinalIgnoreCase
            )
                ? SourceStance.Agrees
                : SourceStance.Dissents,
            _ => SourceStance.Unstated,
        };

    /// <summary>
    /// What the source is worth on the tonic: the recorded kind, or the host when a line
    /// predates the field.
    /// </summary>
    public SourceKind Kind => ParseKind(RecordedKind) ?? KindFromHost;

    /// <summary>
    /// The kind the host implies — a cross-check, not the authority, since a host cannot tell an
    /// analysis from a bare assertion on the same site.
    /// </summary>
    public SourceKind KindFromHost =>
        Host?.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase) switch
        {
            "hooktheory.com" or "musicnotes.com" => SourceKind.Authoritative,
            "tabs.ultimate-guitar.com" or "ultimate-guitar.com" or "songsterr.com" =>
                SourceKind.Corroborating,
            // Automatic chord recognisers. Their chords are evidence; their key is another
            // algorithm's guess, so citing it to confirm ours would be circular.
            "chordify.net" or "chordu.com" => SourceKind.ChordRecogniser,
            "tunebat.com"
            or "songbpm.com"
            or "getsongbpm.com"
            or "getsongkey.com"
            or "songdata.io"
            or "musicgateway.com"
            or "open.spotify.com"
            or "acousticbrainz.org" => SourceKind.Algorithmic,
            null => SourceKind.Unknown,
            _ => SourceKind.Unclassified,
        };

    /// <summary>True when the agent's kind and the host's catalog entry disagree.</summary>
    public bool KindDisagreesWithHost =>
        ParseKind(RecordedKind) is { } recorded
        && KindFromHost is not (SourceKind.Unknown or SourceKind.Unclassified)
        && recorded != KindFromHost;

    /// <summary>How the ledger spells a kind: <c>chord_recogniser</c>.</summary>
    internal static string StoredName(SourceKind kind) =>
        kind switch
        {
            SourceKind.ChordRecogniser => "chord_recogniser",
            _ => kind.ToString().ToLowerInvariant(),
        };

    internal static SourceKind? ParseKind(string? recorded) =>
        recorded?.Trim().ToLowerInvariant() switch
        {
            "authoritative" => SourceKind.Authoritative,
            "corroborating" => SourceKind.Corroborating,
            "chord_recogniser" => SourceKind.ChordRecogniser,
            "algorithmic" => SourceKind.Algorithmic,
            "ear" => SourceKind.Ear,
            _ => null,
        };

    private static Uri? ParseLink(string? url)
    {
        if (url is not { Length: > 0 })
            return null;

        // Agents append notes to the URL itself — " (via web search)" and similar.
        var trimmed = TrailingNote.Replace(url.Trim(), string.Empty).Trim();

        if (
            Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https"
        )
            return absolute;

        // A bare host with no scheme is still a link; a sentence describing a photograph is not.
        return trimmed.Contains(' ') || !trimmed.Contains('.') ? null
            : Uri.TryCreate($"https://{trimmed}", UriKind.Absolute, out var prefixed) ? prefixed
            : null;
    }

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex TrailingNote { get; }
}

/// <summary>Whether a source backed the verdict or argued against it.</summary>
public enum SourceStance
{
    /// <summary>The source said something that is not a key, so there is nothing to compare.</summary>
    Unstated,

    /// <summary>The source named a different key. Not necessarily a problem — an algorithmic
    /// dissent does not demote a verdict — but it is the thing worth seeing first.</summary>
    Dissents,

    /// <summary>The source named the verdict's key.</summary>
    Agrees,
}

/// <summary>
/// What a source is worth on the tonic. Ordered by weight: the view sorts on it, so the
/// strongest evidence leads.
/// </summary>
public enum SourceKind
{
    /// <summary>Neither recorded nor derivable from a host.</summary>
    Unknown,

    /// <summary>A host the catalog does not list and no recorded kind. Read the agent's note.</summary>
    Unclassified,

    /// <summary>
    /// The user's own reading. A hint unless the Ear setting counts it as a human source
    /// (ADR 0055).
    /// </summary>
    Ear,

    /// <summary>Shares this tool's failure modes — corroboration only, never the answer.</summary>
    Algorithmic,

    /// <summary>
    /// An automatic chord recogniser. Its chords are usable evidence; its key may never be
    /// the basis of a verdict, and never lifts one to HIGH.
    /// </summary>
    ChordRecogniser,

    /// <summary>Human transcription — solid, consensus-dependent.</summary>
    Corroborating,

    /// <summary>Human analysis or published sheet music. Authoritative on the tonic.</summary>
    Authoritative,
}

/// <summary>One detector's answer.</summary>
public sealed record AlgorithmResult
{
    public required string Name { get; init; }

    /// <summary>The name on screen: keydetect, madmom, Essentia. <see cref="Name"/> goes in the row's tooltip.</summary>
    public string Label =>
        Name switch
        {
            "ffmpeg keydetect" => "keydetect",
            "Essentia edma" => "Essentia",
            _ => Name,
        };

    public string? Key { get; init; }

    /// <summary>The algorithm's own confidence, where it reports a meaningful one.</summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// CREMA's runner-up. Its winner is always 1.0, so the gap to this is the signal; a near-tie
    /// against the relative key is a swap warning.
    /// </summary>
    public string? RunnerUpKey { get; init; }

    public double? RunnerUpRatio { get; init; }

    /// <summary>keydetect's correlation for its runner-up.</summary>
    public double? RunnerUpConfidence { get; init; }

    /// <summary>Whether keydetect's chord rerank changed its answer.</summary>
    public bool? Reranked { get; init; }

    /// <summary>ffmpeg's banding on its correlation: below 0.70 distrust, above 0.75 usually right.</summary>
    public string? Flag { get; init; }

    public FlagChip? FlagChip => FlagChip.Of(Flag);

    public string? Error { get; init; }

    /// <summary>The detectors a run recorded: keydetect, CREMA and librosa before the port (#113), keydetect, madmom and edma after.</summary>
    internal static IReadOnlyList<AlgorithmResult> FromDto(AlgorithmsDto? dto) =>
        dto is null
            ? []
            :
            [
                .. new AlgorithmResult?[]
                {
                    dto.LibrosaKs is { } librosa
                        ? new()
                        {
                            Name = "librosa (K-S)",
                            Key = librosa.Key,
                            Confidence = librosa.Confidence,
                            Error = librosa.Error,
                        }
                        : null,
                    dto.Crema is { } crema
                        ? new()
                        {
                            Name = "CREMA",
                            Key = crema.Key,
                            RunnerUpKey = crema.RunnerUp?.Key,
                            RunnerUpRatio = crema.RunnerUp?.Ratio,
                            Error = crema.Error,
                        }
                        : null,
                    dto.FfmpegKeydetect is { } keydetect
                        ? new()
                        {
                            Name = "ffmpeg keydetect",
                            Key = keydetect.Key,
                            Confidence = keydetect.R,
                            Flag = keydetect.RFlag,
                            RunnerUpKey = keydetect.KeyAlt,
                            RunnerUpConfidence = keydetect.KeyAltConfidence,
                            Reranked = keydetect.KeyReranked,
                            Error = keydetect.Error,
                        }
                        : null,
                    dto.Madmom is { } madmom
                        ? new()
                        {
                            Name = "madmom",
                            Key = madmom.Key,
                            Confidence = madmom.Confidence,
                            Error = madmom.Error,
                        }
                        : null,
                    dto.EssentiaEdma is { } edma
                        ? new()
                        {
                            Name = "Essentia edma",
                            Key = edma.Key,
                            Confidence = edma.Confidence,
                            Error = edma.Error,
                        }
                        : null,
                }.OfType<AlgorithmResult>(),
            ];
}

/// <summary>Whether two detectors agreed, and how they differed when they did not.</summary>
public sealed record AgreementPair
{
    public required string Pair { get; init; }

    /// <summary>
    /// <c>full</c>, <c>relative</c>, <c>none</c>, or <c>single</c> when only one of the two
    /// produced an answer at all.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>The key the pair settled on, when they settled on one.</summary>
    public string? Recommended { get; init; }

    /// <summary>What each said, when they disagreed outright.</summary>
    public string? Note { get; init; }

    public bool Agrees => Status.Equals("full", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The dangerous outcome: relative keys share every note, so only the cadence or a human
    /// source can break it.
    /// </summary>
    public bool IsRelative => Status.Equals("relative", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A point where the running key estimate changed.</summary>
public sealed record KeyEvent
{
    public double Seconds { get; init; }
    public required string Key { get; init; }
    public double R { get; init; }
}

/// <summary>The evidence file's shape: written by the detectors (#113), and by the Python detectors before them.</summary>
internal sealed record MachineDto
{
    public string? Input { get; init; }

    /// <summary>Start and end in seconds, when only a window was detected.</summary>
    public IReadOnlyList<double>? Segment { get; init; }

    public AlgorithmsDto? Algorithms { get; init; }

    public IReadOnlyList<string>? Backers { get; init; }

    public IReadOnlyList<PairDto>? PairwiseAgreement { get; init; }

    public IReadOnlyList<string>? Chords { get; init; }

    public IReadOnlyList<KeyEventDto>? KeyEvents { get; init; }

    public IReadOnlyDictionary<string, string?>? Versions { get; init; }

    /// <summary>Added when readings were written into a run after its verdict.</summary>
    public LateDto? ReadAfterVerdict { get; init; }

    internal sealed record LateDto
    {
        public DateTimeOffset? At { get; init; }
    }

    /// <summary>Each window's own report, where windows were detected.</summary>
    public IReadOnlyList<WindowDto>? Windows { get; init; }

    internal sealed record WindowDto
    {
        public double Start { get; init; }

        public double End { get; init; }

        public MachineDto? Evidence { get; init; }
    }

    internal sealed record PairDto
    {
        public string? Pair { get; init; }

        public string? Status { get; init; }

        public string? Recommended { get; init; }

        public string? Note { get; init; }
    }

    internal sealed record KeyEventDto
    {
        public double T { get; init; }

        public string? Key { get; init; }

        public double R { get; init; }
    }
}

internal sealed record AlgorithmsDto
{
    public SimpleDto? LibrosaKs { get; init; }

    public CremaDto? Crema { get; init; }

    public FfmpegDto? FfmpegKeydetect { get; init; }

    public SimpleDto? Madmom { get; init; }

    public SimpleDto? EssentiaEdma { get; init; }

    internal sealed record SimpleDto
    {
        public string? Key { get; init; }

        public double? Confidence { get; init; }

        public string? Error { get; init; }
    }

    internal sealed record CremaDto
    {
        public string? Key { get; init; }

        public RunnerUpDto? RunnerUp { get; init; }

        public string? Error { get; init; }

        internal sealed record RunnerUpDto
        {
            public string? Key { get; init; }

            public double? Ratio { get; init; }
        }
    }

    internal sealed record FfmpegDto
    {
        public string? Key { get; init; }

        public double? R { get; init; }

        public string? RFlag { get; init; }

        public string? KeyAlt { get; init; }

        public double? KeyAltConfidence { get; init; }

        public bool? KeyReranked { get; init; }

        public string? Error { get; init; }
    }
}
