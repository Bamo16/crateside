namespace Crateside.Core.Ledger;

/// <summary>One recorded verdict for a song, or a restatement carrying one over to another upload.</summary>
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
    public string? SourceUrl
    {
        get;
        init
        {
            field = value;
            SourceVideoId = YouTubeUrl.TryGetVideoId(value, out var videoId) ? videoId : null;
        }
    }

    /// <summary>
    /// The video ID from <see cref="SourceUrl"/> — the join onto a stem on disk.
    /// </summary>
    public string? SourceVideoId { get; private init; }

    /// <summary>The verdict's primary tonal centre, or null when withheld.</summary>
    public TonalCentre? Centre { get; init; }

    public bool Mixed { get; init; }

    public IReadOnlyList<Section> Sections { get; init; } = [];

    /// <summary>The tagged key in ASCII spelling (<c>G# major</c>): the collapse of <see cref="Centre"/>.</summary>
    public string? Key => Centre?.Collapsed;

    public DiatonicMode? Mode => Centre?.Mode;

    /// <summary>True when the tagged key loses the mode: the track is modal, and <see cref="Key"/> keeps only its tonic.</summary>
    public bool IsCollapsed => Centre is { IsModal: true };

    /// <summary>The key as the song actually plays it (<c>D Dorian</c>), when it is modal.</summary>
    public string? ModalKey => Centre is { IsModal: true, Name: var name } ? name : null;

    public KeyConfidence Confidence { get; init; }

    /// <summary>The old BPM estimate, a warp starting point. Never written by a new run.</summary>
    public double? Bpm { get; init; }

    /// <summary>When this is a restatement: the time of the run whose verdict it carries over.</summary>
    public DateTimeOffset? Restates { get; init; }

    /// <summary>
    /// Whether this run produced a key the user would actually tag with. A LOW verdict may
    /// carry candidates, but only HIGH and MEDIUM count as an answer.
    /// </summary>
    public bool HasTaggableKey =>
        Key is not null && Confidence is KeyConfidence.High or KeyConfidence.Medium;

    internal static KeyConfidence ParseConfidence(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "HIGH" => KeyConfidence.High,
            "MEDIUM" => KeyConfidence.Medium,
            "LOW" => KeyConfidence.Low,
            "WITHHELD" => KeyConfidence.Withheld,
            _ => KeyConfidence.Unknown,
        };
}

/// <summary>How much the judge trusts its own verdict.</summary>
public enum KeyConfidence
{
    /// <summary>A confidence crateside does not know. Treated as no key, and never stored.</summary>
    Unknown,

    /// <summary>No single safe tag was given; <see cref="LedgerRun.Key"/> is null.</summary>
    Withheld,
    Low,
    Medium,
    High,
}
