using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// The Queue settings the user can change from the UI, persisted between runs.
/// </summary>
/// <remarks>
/// <para>
/// Stored in <c>%APPDATA%\crateside\queue.json</c> — outside the working tree, so changing a
/// setting never shows up as a repo modification and nothing user-specific gets committed.
/// </para>
/// <para>
/// This is settings, not state: the staging board stays purely derived, and nothing here
/// records what has been done. A <strong>hold</strong> fits that rule — it is a standing
/// instruction about what to work, like the playlist ID, not a record that anything ran.
/// <strong>The API key is deliberately not here</strong> — it lives in user-secrets and is
/// never written or displayed by the app.
/// </para>
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
                Held = stored?.Held ?? [],
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

    /// <summary>
    /// Holds a track: crateside will not work it until it is released.
    /// </summary>
    /// <remarks>
    /// The case this exists for is a playlist entry that is not the <em>canonical version</em> —
    /// the wrong upload of the right song — noticed long after it was added and not worth
    /// stopping to fix. Keyed by video ID because that is the only identity these tracks have:
    /// they have never been detected, so there is no song identity to key on.
    /// </remarks>
    public QueueSettings Hold(string videoId, string? reason = null) =>
        Save(current =>
            current with
            {
                Held =
                [
                    .. current.Held.Where(h =>
                        !h.VideoId.Equals(videoId, StringComparison.Ordinal)
                    ),
                    new HeldTrack { VideoId = videoId, Reason = reason },
                ],
            }
        );

    /// <summary>Releases a held track back into the work list.</summary>
    public QueueSettings Release(string videoId) =>
        Save(current =>
            current with
            {
                Held =
                [
                    .. current.Held.Where(h =>
                        !h.VideoId.Equals(videoId, StringComparison.Ordinal)
                    ),
                ],
            }
        );

    /// <summary>
    /// Reads, applies, writes. Every mutator goes through here so that saving one setting cannot
    /// drop another — writing a freshly-constructed object would have silently discarded the
    /// holds the moment the playlist ID was changed.
    /// </summary>
    private QueueSettings Save(Func<QueueSettings, QueueSettings> change)
    {
        lock (_gate)
        {
            var settings = change(
                _cached
                    ?? new QueueSettings
                    {
                        PlaylistId = TryReadFile()?.PlaylistId ?? _fallback.PlaylistId,
                        Held = TryReadFile()?.Held ?? [],
                    }
            );

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, SerializerOptions));

            return _cached = settings;
        }
    }

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
    [JsonPropertyName("playlistId")]
    public string? PlaylistId { get; init; }

    /// <summary>Tracks crateside has been told not to work.</summary>
    [JsonPropertyName("held")]
    public IReadOnlyList<HeldTrack> Held { get; init; } = [];
}

/// <summary>A track the user has told crateside to leave alone.</summary>
/// <remarks>
/// A <em>deferral</em>, not a classification: the usual case is knowing an entry is the wrong
/// upload and not wanting to stop and fix it. Whether it is eventually replaced, removed, or
/// released is left open, which is why <see cref="Reason"/> is prose rather than an enum.
/// </remarks>
public sealed record HeldTrack
{
    [JsonPropertyName("videoId")]
    public required string VideoId { get; init; }

    /// <summary>Why, in the user's words. Optional, and free text on purpose.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
