using System.Text.Json.Serialization;

namespace Crateside.Core.Ledger;

/// <summary>
/// One recorded detect-key run — a single line of keytag's <c>results/runs.jsonl</c>, which
/// crateside never writes.
/// </summary>
public sealed record LedgerRun
{
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The song identity — normalized <c>artist - title</c>, lowercased, stem suffix
    /// stripped. What a supersede is filed under.
    /// </summary>
    public required string Song { get; init; }

    public string? Artist { get; init; }
    public string? Title { get; init; }

    /// <summary>
    /// What to call this song on screen — not <see cref="Song"/>, which is a lowercase slug.
    /// Falls back to the identity when a run recorded neither artist nor title.
    /// </summary>
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
    /// The diatonic mode, where a human source established one. Null means nobody looked, not
    /// that the song is plain major/minor.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// True when the tagged key is a <em>collapse</em> of something else — the track is modal,
    /// and <see cref="Key"/> is its parallel major or minor, one note away from what it plays.
    /// </summary>
    public bool IsCollapsed => Mode is not null and not "major" and not "minor";

    /// <summary>The key as the song actually plays it (<c>D Dorian</c>), when that is known.</summary>
    public string? ModalKey =>
        IsCollapsed && Key?.Split(' ') is [var tonic, ..] && Mode is { Length: > 0 } mode
            ? $"{tonic} {char.ToUpperInvariant(mode[0])}{mode[1..]}"
            : null;

    public KeyConfidence Confidence { get; init; }

    /// <summary>The warp starting point, not a tagged value. Rough by design.</summary>
    public double? Bpm { get; init; }

    /// <summary>
    /// Whether this run produced a key the user would actually tag with. A LOW verdict may
    /// carry candidates, but only HIGH and MEDIUM count as an answer.
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
            SourceVideoId = YouTubeUrl.TryGetVideoId(dto.SourceUrl, out var videoId)
                ? videoId
                : null,
            Key = dto.Verdict?.Key,
            Mode = dto.Verdict?.Mode,
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

// Only the fields crateside reads. The `machine` block is deliberately not deserialized.
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
