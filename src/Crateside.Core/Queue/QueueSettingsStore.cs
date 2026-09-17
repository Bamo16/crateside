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
            ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "queue.json");
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
                ConsumeOnArrival = stored?.ConsumeOnArrival is true,
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

    /// <summary>Whether crateside empties the playlist as it takes tracks on.</summary>
    public QueueSettings SaveConsumeOnArrival(bool consume) =>
        Save(current => current with { ConsumeOnArrival = consume });

    /// <summary>
    /// Records which files the user wants made for a track. Null clears the choice, putting the
    /// track back on <see cref="StemPlan.Default"/>.
    /// </summary>
    public QueueSettings SetPlan(string videoId, StemPlan? plan) =>
        Upsert(videoId, t => t with { Plan = plan });

    /// <summary>
    /// Records what the user calls a track. Null or blank clears it, restoring the derived name.
    /// </summary>
    public QueueSettings SetName(string videoId, string? name) =>
        Upsert(
            videoId,
            t => t with { Name = name?.Trim() is { Length: > 0 } given ? given : null }
        );

    /// <summary>
    /// Records the upload to fetch this track from instead of its playlist entry's own. Null or
    /// blank clears it, restoring the entry's video.
    /// </summary>
    /// <summary>Records that crateside should work a track the playlist does not hold.</summary>
    public QueueSettings AddByUrl(string videoId) =>
        Upsert(videoId, track => track with { AddedByUrl = true });

    /// <summary>Drops crateside's own record of a track, which is what removing one means.</summary>
    public QueueSettings Forget(string videoId) =>
        Upsert(videoId, _ => new TrackInstruction { VideoId = videoId });

    public QueueSettings SetSourceUrl(string videoId, string? url) =>
        Upsert(
            videoId,
            t => t with { SourceUrl = url?.Trim() is { Length: > 0 } given ? given : null }
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

    private static IReadOnlyList<TrackInstruction> TracksOf(QueueSettings? stored) =>
        [.. (stored?.Tracks ?? []).Select(Converted)];

    /// <summary>
    /// Turns a pre-<see cref="StemPlan"/> instruction into one: the instrumental-release fact
    /// becomes the plan it used to derive.
    /// </summary>
    /// <remarks>
    /// Clearing the old field is what makes this a conversion rather than a permanent reader —
    /// it is omitted when written, so the file loses it the first time anything saves, and this
    /// can go once no instruction carries it. An instruction that says nothing else is then
    /// dropped by <c>Upsert</c>, which is how the entries recording "has vocals" — the default,
    /// stated — clean themselves up.
    /// </remarks>
    private static TrackInstruction Converted(TrackInstruction stored) =>
        stored is { Plan: null, WasInstrumentalRelease: true }
            ? stored with
            {
                Plan = StemPlan.InstrumentalRelease,
                WasInstrumentalRelease = false,
            }
            : stored with
            {
                WasInstrumentalRelease = false,
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

    /// <summary>
    /// Whether an entry is removed from the playlist as soon as crateside has recorded it
    /// (ADR 0021).
    /// </summary>
    /// <remarks>
    /// Off until asked for. Turning it on removes every entry crateside already knows of on the
    /// next scan, which is not undoable from here.
    /// </remarks>
    [JsonPropertyName("consumeOnArrival")]
    public bool ConsumeOnArrival { get; init; }

    /// <summary>Standing instructions about how to work particular tracks.</summary>
    [JsonPropertyName("tracks")]
    public IReadOnlyList<TrackInstruction> Tracks { get; init; } = [];

    /// <summary>How long the rows may go unrebuilt. Zero means never.</summary>
    public TimeSpan RefreshInterval =>
        TimeSpan.FromMinutes(RefreshMinutes ?? DefaultRefreshMinutes);
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
    /// Which files the user wants made for this track. Null means they have not said, which is
    /// <see cref="StemPlan.Default"/> and is not the same as having chosen it.
    /// </summary>
    [JsonPropertyName("plan")]
    public StemPlan? Plan { get; init; }

    /// <summary>
    /// The pre-<see cref="StemPlan"/> fact, read only so that an existing <c>queue.json</c>
    /// converts itself — see <c>QueueSettingsStore.Converted</c>. Never written: conversion
    /// clears it, and a cleared one is omitted.
    /// </summary>
    [JsonPropertyName("instrumentalRelease")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WasInstrumentalRelease { get; init; }

    /// <summary>
    /// What the user calls this track, which beats every derived name. Null when they have not said.
    /// </summary>
    /// <remarks>A name, not a filename: the files add the preset and extension themselves.</remarks>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The upload to fetch instead of the playlist entry's own video. Null when it still is the
    /// source.
    /// </summary>
    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }

    /// <summary>
    /// crateside was told to work this track by something other than the playlist.
    /// </summary>
    /// <remarks>
    /// The playlist is the inbox (ADR 0010) and this is the second way in, accepted on purpose.
    /// It is what lets a pasted track reach <see cref="QueueView.Outstanding"/>, which a re-cut
    /// deliberately never does.
    /// </remarks>
    [JsonPropertyName("addedByUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AddedByUrl { get; init; }

    /// <summary>Whether this instruction still carries anything worth storing.</summary>
    [JsonIgnore]
    public bool SaysSomething =>
        AddedByUrl || Plan is not null || Name is { Length: > 0 } || SourceUrl is { Length: > 0 };
}
