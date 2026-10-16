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

    /// <summary>
    /// The argument that the tonic is not its relative. Null on older runs, whose prose is in
    /// <see cref="Reasoning"/>.
    /// </summary>
    public string? TonicCheck { get; init; }

    /// <summary>The confidence rationale, and anything else the agent thought worth saying.</summary>
    public string? Reasoning { get; init; }

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

    /// <summary>
    /// Which detectors agreed. Weighted, not counted: two of the same method family agreeing
    /// is weak, because they share their failure modes.
    /// </summary>
    public IReadOnlyList<AgreementPair> Agreement { get; init; } = [];

    /// <summary>The committed chord progression — the cheap check for a relative-key swap.</summary>
    public IReadOnlyList<string> Chords { get; init; } = [];

    /// <summary>Every time the running key estimate changed. Early instability is often just a sparse intro.</summary>
    public IReadOnlyList<KeyEvent> KeyEvents { get; init; } = [];

    /// <summary>keytag's old BPM estimate.</summary>
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
            TonicCheck = run.TonicCheck,
            Reasoning = run.Reasoning,
            InputKind = run.InputKind,
            Sources =
            [
                .. run.Sources.Select(source => new EvidenceSource
                {
                    Url = source.Url,
                    Via = source.Via,
                    States = source.States,
                    StatesKey = source.StatesKey,
                    StatesMode = source.StatesMode?.ToString().ToLowerInvariant(),
                    RecordedKind = EvidenceSource.StoredName(source.Kind),
                    Note = source.Note,
                    Sections = source.Sections,
                    VerdictKey = run.Centre?.Collapsed,
                }),
            ],
            Algorithms = AlgorithmResult.FromDto(machine?.Algorithms),
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

    /// <summary>The verdict this source was consulted for, so agreement can be computed.</summary>
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
    public string? Key { get; init; }

    /// <summary>The algorithm's own confidence, where it reports a meaningful one.</summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// CREMA's runner-up. Its winner is always 1.0, so the gap to this is the signal; a near-tie
    /// against the relative key is a swap warning.
    /// </summary>
    public string? RunnerUpKey { get; init; }

    public double? RunnerUpRatio { get; init; }

    /// <summary>ffmpeg's banding on its correlation: below 0.70 distrust, above 0.75 usually right.</summary>
    public string? Flag { get; init; }

    public string? Error { get; init; }

    internal static IReadOnlyList<AlgorithmResult> FromDto(AlgorithmsDto? dto) =>
        dto is null
            ? []
            :
            [
                new()
                {
                    Name = "librosa (K-S)",
                    Key = dto.LibrosaKs?.Key,
                    Confidence = dto.LibrosaKs?.Confidence,
                    Error = dto.LibrosaKs?.Error,
                },
                new()
                {
                    Name = "CREMA",
                    Key = dto.Crema?.Key,
                    RunnerUpKey = dto.Crema?.RunnerUp?.Key,
                    RunnerUpRatio = dto.Crema?.RunnerUp?.Ratio,
                    Error = dto.Crema?.Error,
                },
                new()
                {
                    Name = "ffmpeg keydetect",
                    Key = dto.FfmpegKeydetect?.Key,
                    Confidence = dto.FfmpegKeydetect?.R,
                    Flag = dto.FfmpegKeydetect?.RFlag,
                    Error = dto.FfmpegKeydetect?.Error,
                },
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

internal sealed record MachineDto
{
    public AlgorithmsDto? Algorithms { get; init; }

    public IReadOnlyList<PairDto>? PairwiseAgreement { get; init; }

    public IReadOnlyList<string>? Chords { get; init; }

    public IReadOnlyList<KeyEventDto>? KeyEvents { get; init; }

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

        public string? Error { get; init; }
    }
}
