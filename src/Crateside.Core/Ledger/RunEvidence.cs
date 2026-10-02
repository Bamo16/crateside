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
    public string? Key { get; init; }

    /// <summary>
    /// The diatonic mode, where a human source established one. Null means nobody looked, not
    /// plain major or minor.
    /// </summary>
    public string? Mode { get; init; }

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

    /// <summary>The warp recommendation.</summary>
    public double? Bpm { get; init; }

    /// <summary>The latest recorded run for a song identity, or null when the ledger has never seen it.</summary>
    public static RunEvidence? ReadLatestFor(string ledgerPath, string song) =>
        ReadLatest(ledgerPath).GetValueOrDefault(song);

    /// <summary>The latest recorded run for every song identity in the ledger, in one pass.</summary>
    public static IReadOnlyDictionary<string, RunEvidence> ReadLatest(string ledgerPath)
    {
        var latest = new Dictionary<string, RunEvidence>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(ledgerPath))
            return latest;

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
                dto = JsonSerializer.Deserialize(
                    line,
                    RunEvidenceJsonContext.Default.RunEvidenceDto
                );
            }
            catch (JsonException)
            {
                continue;
            }

            // A restatement carries the verdict forward under a new source URL and no evidence at
            // all (keytag ADR 0008), so taking the latest *line* would empty this view the first
            // time a track's source was swapped.
            if (dto is not { Song: { } song, Restates: null })
                continue;

            // Append-only, so a later line supersedes an earlier one for the same identity.
            var evidence = FromDto(dto);

            if (!latest.TryGetValue(song, out var held) || evidence.Timestamp >= held.Timestamp)
                latest[song] = evidence;
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
            Artist = dto.Artist,
            Title = dto.Title,
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
        };
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

// Only what the view shows. Deserialized per song on demand, never for the whole ledger.
internal sealed record RunEvidenceDto
{
    public DateTimeOffset? Timestamp { get; init; }

    public string? Song { get; init; }

    /// <summary>
    /// Set on a restatement, naming the run it copies. Its presence is what marks a line as
    /// carrying no evidence of its own (keytag ADR 0008).
    /// </summary>
    public string? Restates { get; init; }

    public string? Artist { get; init; }

    public string? Title { get; init; }

    public VerdictDto? Verdict { get; init; }

    public string? TonicCheck { get; init; }

    public string? Reasoning { get; init; }

    public string? InputKind { get; init; }

    public IReadOnlyList<SourceDto>? Sources { get; init; }

    public BpmDto? Bpm { get; init; }

    public MachineDto? Machine { get; init; }

    internal sealed record VerdictDto
    {
        public string? Key { get; init; }

        public string? Mode { get; init; }
    }

    internal sealed record SourceDto
    {
        public string? Url { get; init; }

        public string? Via { get; init; }

        public string? States { get; init; }

        public string? StatesKey { get; init; }

        public string? StatesMode { get; init; }

        public string? Kind { get; init; }

        public string? Note { get; init; }
    }

    internal sealed record BpmDto
    {
        public double? Value { get; init; }

        public string? Source { get; init; }

        public double? Beatdetect { get; init; }
    }
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
