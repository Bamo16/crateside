using System.Text.Json.Serialization;
using Crateside.Core.Plans;

namespace Crateside.Core.WorkLists;

/// <summary>
/// A standing instruction about how crateside should work one track, keyed by video ID because an
/// undetected track has no song to key on.
/// </summary>
public sealed record TrackInstruction
{
    public required string VideoId { get; init; }

    /// <summary>
    /// Which files the user wants made for this track. Null means they have not said, which is
    /// <see cref="StemPlan.Default"/> and is not the same as having chosen it.
    /// </summary>
    public StemPlan? Plan { get; init; }

    /// <summary>
    /// The pre-<see cref="StemPlan"/> fact, read so an existing <c>queue.json</c> converts itself
    /// and never written back.
    /// </summary>
    [JsonPropertyName("instrumentalRelease")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WasInstrumentalRelease { get; init; }

    /// <summary>
    /// What the user calls this track, which beats every derived name — a name, not a filename.
    /// Null when they have not said.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The upload to fetch instead of the playlist entry's own video. Null when it still is the
    /// source.
    /// </summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// crateside was told to work this track by something other than the playlist (ADR 0010).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AddedByUrl { get; init; }

    /// <summary>
    /// A song the ledger knows that the user said this track is not, though its name looks like
    /// it (#41).
    /// </summary>
    public string? DifferentFrom { get; init; }

    /// <summary>
    /// The upload whose short-of-premium audio the user said to use anyway, kept here because the
    /// working set holding the download does not last (#31).
    /// </summary>
    public string? AcceptedUpload { get; init; }

    /// <summary>
    /// When the user last asked for this track's files to be made again, by a re-cut or a swap —
    /// a request, answered by the outcomes that follow it (#50).
    /// </summary>
    public DateTimeOffset? CutRequestedAt { get; init; }

    /// <summary>Queued from the Library to be judged in the weekly spend-down (ADR 0035, ADR 0063).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Backfill { get; init; }

    /// <summary>Whether this instruction still carries anything worth storing.</summary>
    [JsonIgnore]
    public bool SaysSomething =>
        AddedByUrl
        || Backfill
        || Plan is not null
        || Name is { Length: > 0 }
        || SourceUrl is { Length: > 0 }
        || DifferentFrom is { Length: > 0 }
        || AcceptedUpload is { Length: > 0 }
        || CutRequestedAt is not null;
}
