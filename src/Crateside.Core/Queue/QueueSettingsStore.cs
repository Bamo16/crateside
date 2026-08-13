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
/// records what has been done. <strong>The API key is deliberately not here</strong> — it
/// lives in user-secrets and is never written or displayed by the app.
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
            };
        }
    }

    /// <summary>Saves the playlist ID. Pass null or empty to clear it.</summary>
    public QueueSettings SavePlaylistId(string? playlistId)
    {
        var settings = new QueueSettings { PlaylistId = PlaylistReference.Parse(playlistId) };

        lock (_gate)
        {
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
}
