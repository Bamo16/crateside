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

    /// <summary>The agent's chord/flicker note and confidence rationale. Always present.</summary>
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
            Reasoning = dto.Reasoning,
            InputKind = dto.InputKind,
            Sources =
            [
                .. (dto.Sources ?? []).Select(s => new EvidenceSource
                {
                    Url = s.Url,
                    States = s.States,
                    RecordedTier = s.Tier,
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
    public string? Url { get; init; }

    /// <summary>What the source said the key was.</summary>
    public string? States { get; init; }

    /// <summary>
    /// The tier string as the agent wrote it. Deliberately kept raw: in practice it carries
    /// three different vocabularies plus free prose ("tier-3 (self-inconsistent)"), so it is
    /// the agent's note about the source, not a value to switch on. Use <see cref="Tier"/>
    /// for that.
    /// </summary>
    public string? RecordedTier { get; init; }

    /// <summary>
    /// <see cref="Url"/> as something safe to render as a link, or null when it is not one.
    /// </summary>
    /// <remarks>
    /// The recorded string cannot be linkified blindly, because it is whatever the agent wrote.
    /// Both of these are real entries: <c>"user-provided image"</c> — the user photographed
    /// sheet music, and there is no page to visit — and
    /// <c>"open.spotify.com/track/… (via web search)"</c>, which is a real link wearing a
    /// trailing note and missing its scheme.
    /// </remarks>
    public Uri? Link => ParseLink(Url);

    public string? Host => Link?.Host;

    /// <summary>
    /// What the source actually is, classified from its host rather than from
    /// <see cref="RecordedTier"/>. The host is stable and the catalog in the skill's
    /// <c>SOURCES.md</c> is explicit about what each one is worth; the recorded string is not
    /// reliable enough to style by.
    /// </summary>
    public SourceTier Tier =>
        Host?.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase) switch
        {
            "hooktheory.com" or "musicnotes.com" => SourceTier.Authoritative,
            "tabs.ultimate-guitar.com" or "ultimate-guitar.com" or "songsterr.com" =>
                SourceTier.Corroborating,
            // Automatic chord recognisers. Their chords are evidence; their key is another
            // algorithm's guess, so citing it to confirm ours would be circular.
            "chordify.net" or "chordu.com" => SourceTier.ChordRecogniser,
            "tunebat.com"
            or "songbpm.com"
            or "getsongbpm.com"
            or "getsongkey.com"
            or "songdata.io"
            or "musicgateway.com"
            or "open.spotify.com"
            or "acousticbrainz.org" => SourceTier.Algorithmic,
            null => SourceTier.Unknown,
            _ => SourceTier.Unclassified,
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

/// <summary>What a source is worth on the tonic, by host.</summary>
public enum SourceTier
{
    /// <summary>No URL to judge by.</summary>
    Unknown,

    /// <summary>A host the catalog does not list. Read the agent's note.</summary>
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

    internal sealed record SourceDto
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("states")]
        public string? States { get; init; }

        [JsonPropertyName("tier")]
        public string? Tier { get; init; }
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
