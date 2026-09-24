using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>
/// The Queue settings the user can change from the UI, kept in <c>%APPDATA%\crateside\queue.json</c>.
/// The API key is never here: it lives in user-secrets.
/// </summary>
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

    /// <summary>When the file was last written as of <see cref="_cached"/>, or null for no file.</summary>
    private DateTime? _cachedStamp;

    public QueueSettingsStore(IOptions<QueueOptions> fallback, string? path = null)
    {
        _fallback = fallback.Value;
        _path =
            path
            ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "queue.json");
    }

    /// <summary>
    /// What the user saved, falling back to configuration for a first run. Never throws: an
    /// unreadable file degrades to the fallback.
    /// </summary>
    public QueueSettings Read()
    {
        lock (_gate)
            return Current();
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

    /// <summary>Records that a track is not the ledger song its name looks like (#41).</summary>
    public QueueSettings SetDifferentFrom(string videoId, string song) =>
        Upsert(videoId, track => track with { DifferentFrom = song });

    /// <summary>Records that the user takes this upload's audio as it comes (#31).</summary>
    public QueueSettings AcceptUpload(string videoId, string upload) =>
        Upsert(videoId, track => track with { AcceptedUpload = upload });

    /// <summary>Records that crateside should work a track the playlist does not hold.</summary>
    public QueueSettings AddByUrl(string videoId) =>
        Upsert(videoId, track => track with { AddedByUrl = true });

    /// <summary>Drops crateside's own record of a track, which is what removing one means.</summary>
    public QueueSettings Forget(string videoId) =>
        Upsert(videoId, _ => new TrackInstruction { VideoId = videoId });

    /// <summary>
    /// Records the upload to fetch this track from instead of its playlist entry's own. Null or
    /// blank clears it, restoring the entry's video.
    /// </summary>
    public QueueSettings SetSourceUrl(string videoId, string? url) =>
        Upsert(
            videoId,
            t => t with { SourceUrl = url?.Trim() is { Length: > 0 } given ? given : null }
        );

    /// <summary>
    /// Applies a change to one track's instruction, adding it if there was none and dropping it
    /// again once it says nothing.
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
    /// Reads, applies, writes — the one path every mutator takes, so saving one setting cannot
    /// drop another.
    /// </summary>
    private QueueSettings Save(Func<QueueSettings, QueueSettings> change)
    {
        lock (_gate)
        {
            var settings = change(Current());

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, SerializerOptions));

            _cachedStamp = StampOf(_path);

            return _cached = settings;
        }
    }

    /// <summary>
    /// The settings as the file has them now, re-read when its stamp shows an edit from outside.
    /// Callers hold <see cref="_gate"/>.
    /// </summary>
    private QueueSettings Current()
    {
        var stamp = StampOf(_path);

        if (_cached is { } cached && stamp == _cachedStamp)
            return cached;

        var stored = TryReadFile();

        _cachedStamp = stamp;

        return _cached = new QueueSettings
        {
            PlaylistId = stored?.PlaylistId ?? _fallback.PlaylistId,
            RefreshMinutes = stored?.RefreshMinutes,
            ConsumeOnArrival = stored?.ConsumeOnArrival is true,
            Tracks = TracksOf(stored),
        };
    }

    private static DateTime? StampOf(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;

    private static IReadOnlyList<TrackInstruction> TracksOf(QueueSettings? stored) =>
        [.. (stored?.Tracks ?? []).Select(Converted)];

    /// <summary>
    /// Turns a pre-<see cref="StemPlan"/> instruction into one, clearing the old field so this
    /// can go once no instruction carries it.
    /// </summary>
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
    /// How often to rebuild the rows unprompted, in minutes; zero is never. Stage workers read
    /// the queue on their own loop regardless.
    /// </summary>
    [JsonPropertyName("refreshMinutes")]
    public int? RefreshMinutes { get; init; }

    /// <summary>
    /// Whether an entry leaves the playlist once recorded; turning it on removes every known entry
    /// on the next scan, for good (ADR 0021).
    /// </summary>
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
/// A standing instruction about how crateside should work one track, keyed by video ID because an
/// undetected track has no song to key on.
/// </summary>
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
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The upload to fetch instead of the playlist entry's own video. Null when it still is the
    /// source.
    /// </summary>
    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }

    /// <summary>
    /// crateside was told to work this track by something other than the playlist (ADR 0010).
    /// </summary>
    [JsonPropertyName("addedByUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AddedByUrl { get; init; }

    /// <summary>
    /// A song the ledger knows that the user said this track is not, though its name looks like
    /// it (#41).
    /// </summary>
    [JsonPropertyName("differentFrom")]
    public string? DifferentFrom { get; init; }

    /// <summary>
    /// The upload whose short-of-premium audio the user said to use anyway, kept here because the
    /// working set holding the download does not last (#31).
    /// </summary>
    [JsonPropertyName("acceptedUpload")]
    public string? AcceptedUpload { get; init; }

    /// <summary>Whether this instruction still carries anything worth storing.</summary>
    [JsonIgnore]
    public bool SaysSomething =>
        AddedByUrl
        || Plan is not null
        || Name is { Length: > 0 }
        || SourceUrl is { Length: > 0 }
        || DifferentFrom is { Length: > 0 }
        || AcceptedUpload is { Length: > 0 };
}
