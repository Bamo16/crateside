using System.Text.Json.Serialization;
using Crateside.Core.Staging;

namespace Crateside.Core.Ledger;

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

    /// <summary>Usually a YouTube Music URL. Null for runs recorded from a local file.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// The video ID from <see cref="SourceUrl"/> — the join onto a stem on disk.
    /// </summary>
    public string? SourceVideoId { get; init; }

    /// <summary>The tagged key in ASCII spelling (<c>G# major</c>), or null when withheld.</summary>
    public string? Key { get; init; }

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
            Artist = dto.Artist,
            Title = dto.Title,
            SourceUrl = dto.SourceUrl,
            SourceVideoId = StemProvenance.ExtractYouTubeVideoId(dto.SourceUrl),
            Key = dto.Verdict?.Key,
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

    internal sealed record VerdictDto
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("confidence")]
        public string? Confidence { get; init; }
    }

    internal sealed record BpmDto
    {
        [JsonPropertyName("value")]
        public double? Value { get; init; }
    }
}
