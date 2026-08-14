using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ledger;

/// <summary>
/// The full evidence behind one verdict: what the agent concluded and why, the sources it
/// found, and everything the three detectors reported.
/// </summary>
/// <remarks>
/// <para>
/// Read on demand for a single song rather than with the whole ledger. The <c>machine</c> block
/// is the bulk of every line and nothing on the board needs it, so <see cref="KeyLedger"/>
/// deliberately does not deserialize it — this streams the file and parses only the one line
/// asked for, which keeps that hot path as cheap as it was.
/// </para>
/// <para>
/// This is keytag's <strong>evidence and judgment</strong> split as it appears in a record: the
/// machine layer is what the runner produced, the agent layer is what was concluded from it.
/// The view should keep them visibly distinct for the same reason the record does.
/// </para>
/// </remarks>
public sealed record RunEvidence
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public required DateTimeOffset Timestamp { get; init; }
    public required string Song { get; init; }

    /// <summary>The verdict's key, so a source's own answer can be compared against it.</summary>
    public string? Key { get; init; }

    /// <summary>
    /// The diatonic mode, where a human source established one. Null means <em>nobody looked</em>
    /// — not "plain major/minor". No detector can produce a mode, so this is always a judgment.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// The argument that the tonic is not its relative, made against the chords and the flicker.
    /// Its own field because it is read <em>next to</em> the chords it argues about. Null on runs
    /// recorded before the schema split it out — their prose is still in <see cref="Reasoning"/>.
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
    /// External sources consulted. Empty for about a third of runs, which is a fact about the
    /// catalog rather than a defect — underground and non-Anglophone releases often have no
    /// human coverage at all.
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

    /// <summary>The warp recommendation.</summary>
    public double? Bpm { get; init; }

    public string? BpmSource { get; init; }

    /// <summary>
    /// The runner's own tempo estimate, always kept beside the recommendation. Frequently wrong
    /// — especially by a factor of two — so it is shown as a cross-check, never as the answer.
    /// </summary>
    public double? BeatdetectBpm { get; init; }

    /// <summary>True when the two BPM figures are not a clean tempo-octave apart.</summary>
    /// <remarks>
    /// A 2× difference is just a choice of tempo octave. Anything else means one of them is
    /// simply wrong, which is worth flagging rather than reconciling.
    /// </remarks>
    public bool BpmConflicts =>
        Bpm is { } bpm and > 0
        && BeatdetectBpm is { } rough and > 0
        && Math.Abs(bpm - rough) > 1
        && Math.Abs(Math.Log2(rough / bpm) - Math.Round(Math.Log2(rough / bpm))) > 0.02;

    /// <summary>
    /// The latest recorded run for a song identity, or null when the ledger has never seen it.
    /// </summary>
    public static RunEvidence? ReadLatestFor(string ledgerPath, string song)
    {
        if (!File.Exists(ledgerPath))
            return null;

        RunEvidence? latest = null;

        using var stream = new FileStream(
            ledgerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.AsSpan().IsWhiteSpace())
                continue;

            RunEvidenceDto? dto;

            try
            {
                dto = JsonSerializer.Deserialize<RunEvidenceDto>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (
                dto?.Song is not { } found
                || !found.Equals(song, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            // Append-only, so a later line supersedes an earlier one for the same identity.
            var evidence = FromDto(dto);

            if (latest is null || evidence.Timestamp >= latest.Timestamp)
                latest = evidence;
        }

        return latest;
    }

    private static RunEvidence FromDto(RunEvidenceDto dto) =>
        new()
        {
            Timestamp = dto.Timestamp ?? default,
            Song = dto.Song ?? string.Empty,
            Key = dto.Verdict?.Key,
            Mode = dto.Verdict?.Mode,
            TonicCheck = dto.TonicCheck,
            Reasoning = dto.Reasoning,
            InputKind = dto.InputKind,
            Sources =
            [
                .. (dto.Sources ?? []).Select(s => new EvidenceSource
                {
                    Url = s.Url,
                    Via = s.Via,
                    States = s.States,
                    StatesKey = s.StatesKey,
                    StatesMode = s.StatesMode,
                    RecordedKind = s.Kind,
                    Note = s.Note,
                    VerdictKey = dto.Verdict?.Key,
                }),
            ],
            Algorithms = AlgorithmResult.FromDto(dto.Machine?.Algorithms),
            Agreement =
            [
                .. (dto.Machine?.PairwiseAgreement ?? []).Select(p => new AgreementPair
                {
                    Pair = p.Pair ?? string.Empty,
                    Status = p.Status ?? "none",
                    Recommended = p.Recommended,
                    Note = p.Note,
                }),
            ],
            Chords = dto.Machine?.Chords ?? [],
            KeyEvents =
            [
                .. (dto.Machine?.KeyEvents ?? []).Select(e => new KeyEvent
                {
                    Seconds = e.T,
                    Key = e.Key ?? string.Empty,
                    R = e.R,
                }),
            ],
            Bpm = dto.Bpm?.Value,
            BpmSource = dto.Bpm?.Source,
            BeatdetectBpm = dto.Bpm?.Beatdetect,
        };
}

/// <summary>An external source consulted for a verdict.</summary>
public sealed partial record EvidenceSource
{
    /// <summary>The page, or null when there was none to visit.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// How the source was obtained, when that is not simply "this link". Carries the case a
    /// URL cannot: <c>"user-provided image"</c> is the user photographing sheet music, which
    /// is among the strongest evidence in the record and has no page at all.
    /// </summary>
    public string? Via { get; init; }

    /// <summary>Everything the source said about the key, verbatim.</summary>
    public string? States { get; init; }

    /// <summary>
    /// What the source said, in the verdict's own spelling — so it compares. Null when the
    /// source said something that is not a key, which is a fifth of them.
    /// </summary>
    public string? StatesKey { get; init; }

    /// <summary>The diatonic mode the source named, where it named one.</summary>
    public string? StatesMode { get; init; }

    /// <summary>The kind as recorded, from the ledger's closed set.</summary>
    public string? RecordedKind { get; init; }

    /// <summary>
    /// Anything else the agent said about this source: how strong it is, and how a dissent
    /// was handled. The half of the old <c>tier</c> field that was worth keeping.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>The verdict this source was consulted for, so agreement can be computed.</summary>
    public string? VerdictKey { get; init; }

    /// <summary>
    /// <see cref="Url"/> as something safe to render as a link, or null when it is not one.
    /// </summary>
    /// <remarks>
    /// The recorder now splits the link from its provenance on write, so this is defensive
    /// rather than load-bearing. It stays because a line written by an older recorder still
    /// reads, and a dead anchor is a worse failure than a plain string.
    /// </remarks>
    public Uri? Link => ParseLink(Url);

    public string? Host => Link?.Host;

    /// <summary>
    /// Whether this source agreed with the verdict — the single most useful thing the sources
    /// list can show, and what the schema split was mostly for.
    /// </summary>
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
    /// The kind the host implies. Kept as a <em>cross-check</em>, not the authority: a host
    /// catalog cannot classify a photograph of sheet music, cannot tell a rigorous analysis
    /// from a bare assertion on the same blog, and cannot cover an unbounded set of domains.
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
    /// <remarks>
    /// Worth surfacing rather than reconciling. Filing Chordify as anything but a chord
    /// recogniser is precisely the mistake <c>SOURCES.md</c>'s hard rule exists to prevent,
    /// and it was made — three HIGH verdicts once rested on a chord recogniser's key.
    /// </remarks>
    public bool KindDisagreesWithHost =>
        ParseKind(RecordedKind) is { } recorded
        && KindFromHost is not (SourceKind.Unknown or SourceKind.Unclassified)
        && recorded != KindFromHost;

    private static SourceKind? ParseKind(string? recorded) =>
        recorded?.Trim().ToLowerInvariant() switch
        {
            "authoritative" => SourceKind.Authoritative,
            "corroborating" => SourceKind.Corroborating,
            "chord_recogniser" => SourceKind.ChordRecogniser,
            "algorithmic" => SourceKind.Algorithmic,
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

    /// <summary>Shares this tool's failure modes — corroboration only, never the answer.</summary>
    Algorithmic,

    /// <summary>
    /// An automatic chord recogniser. Its chords are usable evidence; <strong>its key may
    /// never be the basis of a verdict</strong>, and never lifts one to HIGH.
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
    /// CREMA's runner-up and how close it was. CREMA has no real confidence — its winner's
    /// value is structurally always 1.0 — so separation from the runner-up is the actual
    /// signal, and a near-tie against the relative key is a swap warning.
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
    /// The dangerous outcome: relative keys share every note, so this cannot be broken by any
    /// confidence value — only by the cadence or a human source.
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

// Only what the view shows. Deserialized per song on demand, never for the whole ledger.
internal sealed record RunEvidenceDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    [JsonPropertyName("song")]
    public string? Song { get; init; }

    [JsonPropertyName("verdict")]
    public VerdictDto? Verdict { get; init; }

    [JsonPropertyName("tonic_check")]
    public string? TonicCheck { get; init; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }

    [JsonPropertyName("input_kind")]
    public string? InputKind { get; init; }

    [JsonPropertyName("sources")]
    public IReadOnlyList<SourceDto>? Sources { get; init; }

    [JsonPropertyName("bpm")]
    public BpmDto? Bpm { get; init; }

    [JsonPropertyName("machine")]
    public MachineDto? Machine { get; init; }

    internal sealed record VerdictDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("mode")]
        public string? Mode { get; init; }
    }

    internal sealed record SourceDto
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("via")]
        public string? Via { get; init; }

        [JsonPropertyName("states")]
        public string? States { get; init; }

        [JsonPropertyName("states_key")]
        public string? StatesKey { get; init; }

        [JsonPropertyName("states_mode")]
        public string? StatesMode { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("note")]
        public string? Note { get; init; }
    }

    internal sealed record BpmDto
    {
        [JsonPropertyName("value")]
        public double? Value { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("beatdetect")]
        public double? Beatdetect { get; init; }
    }
}

internal sealed record MachineDto
{
    [JsonPropertyName("algorithms")]
    public AlgorithmsDto? Algorithms { get; init; }

    [JsonPropertyName("pairwise_agreement")]
    public IReadOnlyList<PairDto>? PairwiseAgreement { get; init; }

    [JsonPropertyName("chords")]
    public IReadOnlyList<string>? Chords { get; init; }

    [JsonPropertyName("key_events")]
    public IReadOnlyList<KeyEventDto>? KeyEvents { get; init; }

    internal sealed record PairDto
    {
        [JsonPropertyName("pair")]
        public string? Pair { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("recommended")]
        public string? Recommended { get; init; }

        [JsonPropertyName("note")]
        public string? Note { get; init; }
    }

    internal sealed record KeyEventDto
    {
        [JsonPropertyName("t")]
        public double T { get; init; }

        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("r")]
        public double R { get; init; }
    }
}

internal sealed record AlgorithmsDto
{
    [JsonPropertyName("librosa_ks")]
    public SimpleDto? LibrosaKs { get; init; }

    [JsonPropertyName("crema")]
    public CremaDto? Crema { get; init; }

    [JsonPropertyName("ffmpeg_keydetect")]
    public FfmpegDto? FfmpegKeydetect { get; init; }

    internal sealed record SimpleDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }

    internal sealed record CremaDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("runner_up")]
        public RunnerUpDto? RunnerUp { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        internal sealed record RunnerUpDto
        {
            [JsonPropertyName("key")]
            public string? Key { get; init; }

            [JsonPropertyName("ratio")]
            public double? Ratio { get; init; }
        }
    }

    internal sealed record FfmpegDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("r")]
        public double? R { get; init; }

        [JsonPropertyName("r_flag")]
        public string? RFlag { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}
