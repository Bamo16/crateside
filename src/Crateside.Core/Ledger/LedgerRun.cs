using System.Text.Json.Serialization;
using Crateside.Core.Staging;

namespace Crateside.Core.Ledger;

/// <summary>
/// One recorded detect-key run — a single line of keytag's <c>results/runs.jsonl</c>.
/// </summary>
/// <remarks>
/// The file is append-only and a re-run is a <em>supersede</em>: the latest line for a given
/// <see cref="Song"/> wins. crateside reads it and never writes it.
/// </remarks>
public sealed record LedgerRun
{
    /// <summary>When the run was recorded.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The song identity — normalized <c>artist - title</c>, lowercased, stem suffix
    /// stripped. This, not the URL and not the filename, is what a supersede is filed under.
    /// </summary>
    public required string Song { get; init; }

    public string? Artist { get; init; }
    public string? Title { get; init; }

    /// <summary>
    /// What to call this song on screen.
    /// </summary>
    /// <remarks>
    /// <strong>Not <see cref="Song"/></strong>, which is the normalized lowercase identity built
    /// for matching — it reads as a slug, because that is what it is. The artist and title are
    /// recorded separately and properly cased, and joining them is the only way to get a name a
    /// person would recognise. Falls back to the identity when a run recorded neither.
    /// </remarks>
    public string DisplayName =>
        this is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
            ? $"{artist} - {title}"
            : Song;

    /// <summary>Usually a YouTube Music URL. Null for runs recorded from a local file.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The video ID from <see cref="SourceUrl"/> — the join onto a stem on disk.
    /// </summary>
    public string? SourceVideoId { get; init; }

    /// <summary>The tagged key in ASCII spelling (<c>G# major</c>), or null when withheld.</summary>
    public string? Key { get; init; }

    /// <summary>
    /// The diatonic mode, where a human source established one. Null means <em>nobody looked</em>,
    /// which is true of most runs — not a claim that the song is plain major/minor.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// True when the tagged key is a <em>collapse</em> of something else — the track is modal,
    /// and <see cref="Key"/> is its parallel major or minor.
    /// </summary>
    /// <remarks>
    /// Worth a marker on the board rather than only on the evidence page. A collapsed key is one
    /// note away from what the track actually plays (Dorian's natural sixth, Mixolydian's flat
    /// seventh), so it is a candidate explanation when a pairing sounds wrong — and the moment
    /// you need it, you do not yet know to go looking for it.
    /// </remarks>
    public bool IsCollapsed => Mode is not null and not "major" and not "minor";

    /// <summary>The key as the song actually plays it (<c>D Dorian</c>), when that is known.</summary>
    /// <remarks>
    /// Reconstructed rather than stored: the collapse keeps the tonic, so the tagged key and the
    /// mode together restate the original exactly and a second field could only drift.
    /// </remarks>
    public string? ModalKey =>
        IsCollapsed && Key?.Split(' ') is [var tonic, ..] && Mode is { Length: > 0 } mode
            ? $"{tonic} {char.ToUpperInvariant(mode[0])}{mode[1..]}"
            : null;

    /// <summary>
    /// False when the user stated the release is instrumental: nothing to separate out, so the
    /// drums are the only stem and the source track is the file that gets kept and tagged.
    /// </summary>
    /// <remarks>
    /// Null means <em>not stated</em>, not "it has vocals" — no run recorded before the field
    /// existed said anything. What to do about not-stated is this consumer's policy, and the
    /// policy is to expect the usual vocal and instrumental pair.
    /// </remarks>
    public bool? HasVocals { get; init; }

    public KeyConfidence Confidence { get; init; }

    /// <summary>The warp starting point, not a tagged value. Rough by design.</summary>
    public double? Bpm { get; init; }

    /// <summary>
    /// Whether this run produced a key the user would actually tag with. A LOW verdict may
    /// still carry candidates, but the skill's rule is that a wrong tag is worse than no
    /// tag — so only HIGH and MEDIUM count as an answer.
    /// </summary>
    public bool HasTaggableKey =>
        Key is not null && Confidence is KeyConfidence.High or KeyConfidence.Medium;

    internal static LedgerRun? FromDto(LedgerRunDto dto)
    {
        if (dto.Timestamp is null || dto.Song is not { Length: > 0 } song)
            return null;

        return new LedgerRun
        {
            Timestamp = dto.Timestamp.Value,
            Song = song,
            Artist = LedgerText.Repaired(dto.Artist),
            Title = LedgerText.Repaired(dto.Title),
            SourceUrl = dto.SourceUrl,
            SourceVideoId = StemProvenance.ExtractYouTubeVideoId(dto.SourceUrl),
            Key = dto.Verdict?.Key,
            Mode = dto.Verdict?.Mode,
            HasVocals = dto.HasVocals,
            Confidence = ParseConfidence(dto.Verdict?.Confidence),
            Bpm = dto.Bpm?.Value,
        };
    }

    private static KeyConfidence ParseConfidence(string? value) =>
        value?.ToUpperInvariant() switch
        {
            "HIGH" => KeyConfidence.High,
            "MEDIUM" => KeyConfidence.Medium,
            "LOW" => KeyConfidence.Low,
            "WITHHELD" => KeyConfidence.Withheld,
            _ => KeyConfidence.Unknown,
        };
}

/// <summary>How much the detect-key run trusts its own verdict.</summary>
public enum KeyConfidence
{
    /// <summary>The line carried a confidence crateside does not know. Treated as no key.</summary>
    Unknown,

    /// <summary>No single safe tag was given; <see cref="LedgerRun.Key"/> is null.</summary>
    Withheld,
    Low,
    Medium,
    High,
}

// Only the fields crateside reads. The `machine` block — three algorithms, chords, flicker —
// is deliberately not deserialized: it is the bulk of every line and nothing here uses it.
internal sealed record LedgerRunDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    [JsonPropertyName("song")]
    public string? Song { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; init; }

    [JsonPropertyName("verdict")]
    public VerdictDto? Verdict { get; init; }

    [JsonPropertyName("bpm")]
    public BpmDto? Bpm { get; init; }

    [JsonPropertyName("has_vocals")]
    public bool? HasVocals { get; init; }

    internal sealed record VerdictDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("mode")]
        public string? Mode { get; init; }

        [JsonPropertyName("confidence")]
        public string? Confidence { get; init; }
    }

    internal sealed record BpmDto
    {
        [JsonPropertyName("value")]
        public double? Value { get; init; }
    }
}
