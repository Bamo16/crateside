using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// The Queue settings the user can change from the UI, persisted between runs.
/// </summary>
/// <remarks>
/// Stored in <c>%APPDATA%\crateside\queue.json</c>, outside the working tree. The API key is
/// deliberately not here — it lives in user-secrets and is never written or displayed by the app.
/// </remarks>
public sealed class QueueSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly QueueOptions _fallback;
    private readonly Lock _gate = new();

    private QueueSettings? _cached;

    public QueueSettingsStore(IOptions<QueueOptions> fallback, string? path = null)
    {
        _fallback = fallback.Value;
        _path =
            path
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "crateside",
                "queue.json"
            );
    }

    /// <summary>
    /// The current settings: what the user saved, falling back to configuration for a first
    /// run. Never throws — an unreadable or corrupt file degrades to the fallback.
    /// </summary>
    public QueueSettings Read()
    {
        lock (_gate)
        {
            if (_cached is not null)
                return _cached;

            var stored = TryReadFile();

            return _cached = new QueueSettings
            {
                PlaylistId = stored?.PlaylistId ?? _fallback.PlaylistId,
                RefreshMinutes = stored?.RefreshMinutes,
                Tracks = TracksOf(stored),
            };
        }
    }

    /// <summary>Saves the playlist ID. Pass null or empty to clear it.</summary>
    public QueueSettings SavePlaylistId(string? playlistId) =>
        Save(current =>
            current with
            {
                PlaylistId = PlaylistReference.TryParse(playlistId, out var parsed) ? parsed : null,
            }
        );

    /// <summary>How often to look at the playlist unprompted. Null restores the default.</summary>
    public QueueSettings SaveRefreshMinutes(int? minutes) =>
        Save(current => current with { RefreshMinutes = minutes });

    /// <summary>Holds a track: crateside will not work it until it is released.</summary>
    public QueueSettings Hold(string videoId, string? reason = null) =>
        Upsert(videoId, t => t with { IsHeld = true, HoldReason = reason });

    /// <summary>Releases a held track back into the work list.</summary>
    public QueueSettings Release(string videoId) =>
        Upsert(videoId, t => t with { IsHeld = false, HoldReason = null });

    /// <summary>
    /// Records that a track is an instrumental release — no vocals to begin with, so there is
    /// nothing to separate out and the source track is the sample.
    /// </summary>
    /// <remarks>Reaches keytag's ledger as <c>has_vocals</c> (ADR 0004).</remarks>
    public QueueSettings SetInstrumentalRelease(string videoId, bool isInstrumentalRelease) =>
        Upsert(videoId, t => t with { IsInstrumentalRelease = isInstrumentalRelease });

    /// <summary>
    /// Records what the user calls a track. Null or blank clears it, restoring the derived name.
    /// </summary>
    public QueueSettings SetName(string videoId, string? name) =>
        Upsert(
            videoId,
            t => t with { Name = name?.Trim() is { Length: > 0 } given ? given : null }
        );

    /// <summary>
    /// Applies a change to one track's instruction, adding it if there was none and dropping it
    /// again once it says nothing. Keeping empty entries would turn every released hold into a
    /// permanent line in the file.
    /// </summary>
    private QueueSettings Upsert(string videoId, Func<TrackInstruction, TrackInstruction> change)
    {
        return Save(current =>
        {
            var existing =
                current.Tracks.FirstOrDefault(t =>
                    t.VideoId.Equals(videoId, StringComparison.Ordinal)
                ) ?? new TrackInstruction { VideoId = videoId };

            var changed = change(existing);

            return current with
            {
                Tracks =
                [
                    .. current.Tracks.Where(t =>
                        !t.VideoId.Equals(videoId, StringComparison.Ordinal)
                    ),
                    .. changed.SaysSomething ? new[] { changed } : [],
                ],
            };
        });
    }

    /// <summary>
    /// Reads, applies, writes. Every mutator goes through here so that saving one setting cannot
    /// drop another — writing a freshly-constructed object would have silently discarded the
    /// holds the moment the playlist ID was changed.
    /// </summary>
    private QueueSettings Save(Func<QueueSettings, QueueSettings> change)
    {
        lock (_gate)
        {
            var stored = _cached is null ? TryReadFile() : null;

            var settings = change(
                _cached
                    ?? new QueueSettings
                    {
                        PlaylistId = stored?.PlaylistId ?? _fallback.PlaylistId,
                        Tracks = TracksOf(stored),
                    }
            );

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, SerializerOptions));

            return _cached = settings;
        }
    }

    /// <summary>
    /// The stored instructions, reading a file written before holds and the instrumental-release
    /// fact became one entry. A file with only the old <c>held</c> array still describes real
    /// user decisions, so it is read rather than discarded; the next write puts it in the new
    /// shape.
    /// </summary>
    private static IReadOnlyList<TrackInstruction> TracksOf(QueueSettings? stored) =>
        stored switch
        {
            { Tracks: { Count: > 0 } tracks } => tracks,
            { Held: { Count: > 0 } held } =>
            [
                .. held.Select(h => new TrackInstruction
                {
                    VideoId = h.VideoId,
                    IsHeld = true,
                    HoldReason = h.Reason,
                }),
            ],
            _ => [],
        };

    private QueueSettings? TryReadFile()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<QueueSettings>(
                    File.ReadAllText(_path),
                    SerializerOptions
                )
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>User-editable Queue settings. Contains no secrets.</summary>
public sealed record QueueSettings
{
    /// <summary>What <see cref="RefreshMinutes"/> means when nobody has said.</summary>
    public const int DefaultRefreshMinutes = 5;

    [JsonPropertyName("playlistId")]
    public string? PlaylistId { get; init; }

    /// <summary>
    /// How often to rebuild the rows with nothing having been announced, in minutes. Zero is
    /// never; null is <see cref="QueueSettings.DefaultRefreshMinutes"/>.
    /// </summary>
    /// <remarks>
    /// About the view being true, not about work starting — stage workers read the queue on
    /// their own idle loop regardless of this setting.
    /// </remarks>
    [JsonPropertyName("refreshMinutes")]
    public int? RefreshMinutes { get; init; }

    /// <summary>Standing instructions about how to work particular tracks.</summary>
    [JsonPropertyName("tracks")]
    public IReadOnlyList<TrackInstruction> Tracks { get; init; } = [];

    /// <summary>How long the rows may go unrebuilt. Zero means never.</summary>
    public TimeSpan RefreshInterval =>
        TimeSpan.FromMinutes(RefreshMinutes ?? DefaultRefreshMinutes);

    /// <summary>
    /// The previous shape, read once so existing holds survive and then never written again.
    /// </summary>
    [JsonPropertyName("held")]
    public IReadOnlyList<HeldTrack>? Held { get; init; }
}

/// <summary>
/// A standing instruction about how crateside should work one track.
/// </summary>
/// <remarks>
/// Keyed by video ID because that is the only identity these tracks have — an undetected
/// track has no song identity to key on.
/// </remarks>
public sealed record TrackInstruction
{
    [JsonPropertyName("videoId")]
    public required string VideoId { get; init; }

    /// <summary>
    /// crateside will not work this track at all until it is released.
    /// </summary>
    /// <remarks>
    /// A deferral, not a classification — whether it is eventually replaced, removed, or
    /// released is left open, which is why <see cref="HoldReason"/> is prose.
    /// </remarks>
    [JsonPropertyName("held")]
    public bool IsHeld { get; init; }

    /// <summary>Why, in the user's words. Optional, and free text on purpose.</summary>
    [JsonPropertyName("holdReason")]
    public string? HoldReason { get; init; }

    /// <summary>
    /// The release has no vocals to begin with, so there is nothing to separate out: the drums
    /// are the only stem and the source track is the sample.
    /// </summary>
    /// <remarks>
    /// The user's statement about the release, never crateside's inference — not from the audio,
    /// and not from a title containing the word "Instrumental", which also describes an official
    /// instrumental of a track that does have vocals.
    /// </remarks>
    [JsonPropertyName("instrumentalRelease")]
    public bool IsInstrumentalRelease { get; init; }

    /// <summary>
    /// What the user calls this track, which beats every derived name. Null when they have not said.
    /// </summary>
    /// <remarks>A name, not a filename: the files add the preset and extension themselves.</remarks>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Whether this instruction still carries anything worth storing.</summary>
    [JsonIgnore]
    public bool SaysSomething => IsHeld || IsInstrumentalRelease || Name is { Length: > 0 };
}

/// <summary>A held track as the previous file format wrote it. Read-only; see ADR 0005.</summary>
public sealed record HeldTrack
{
    [JsonPropertyName("videoId")]
    public required string VideoId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
