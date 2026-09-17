using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Queue;

/// <summary>
/// What crateside keeps about a playlist entry so that it never has to read the entry again
/// (ADR 0021).
/// </summary>
/// <remarks>
/// Stored in <c>%APPDATA%\crateside\intake.json</c>, beside the settings and apart from them.
/// </remarks>
public sealed class IntakeStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    private IReadOnlyList<IntakeRecord>? _cached;
    private bool _durable = true;

    /// <summary>
    /// Whether everything recorded has reached disk.
    /// </summary>
    /// <remarks>
    /// False once a write has failed, and it does not recover within a run: a record that exists
    /// only in memory is lost at the next restart.
    /// </remarks>
    public bool IsDurable
    {
        get
        {
            lock (_gate)
                return _durable;
        }
    }

    public IntakeStore(string? path = null) =>
        _path =
            path
            ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "intake.json");

    /// <summary>
    /// Every track crateside has taken on. Never throws — an unreadable file reads as empty,
    /// which costs a re-read of the playlist rather than losing anything.
    /// </summary>
    public IReadOnlyList<IntakeRecord> Read()
    {
        lock (_gate)
            return _cached ??= TryReadFile() ?? [];
    }

    /// <summary>
    /// Remembers every track it has not seen before, and returns the ones it added.
    /// </summary>
    /// <remarks>
    /// Existing records are left alone rather than refreshed: a title the user has since changed
    /// must not be undone by a later scan.
    /// </remarks>
    public IReadOnlyList<IntakeRecord> Record(IEnumerable<QueueItem> items)
    {
        lock (_gate)
        {
            var known = Read().Select(r => r.VideoId).ToHashSet(StringComparer.Ordinal);
            var added = items
                .Where(item => known.Add(item.VideoId))
                .Select(item => new IntakeRecord
                {
                    VideoId = item.VideoId,
                    Title = item.Title,
                    AddedAt = item.AddedAt,
                    PremiumOutlook = item.PremiumOutlook,
                    IsUnavailable = item.IsUnavailable,
                })
                .ToList();

            if (added.Count is 0)
                return [];

            Write([.. Read(), .. added]);

            return added;
        }
    }

    /// <summary>
    /// Takes tracks crateside already knows of into the intake, for a store that predates it.
    /// </summary>
    public void Backfill(IEnumerable<string> videoIds)
    {
        lock (_gate)
        {
            var known = Read().Select(r => r.VideoId).ToHashSet(StringComparer.Ordinal);
            var added = videoIds
                .Where(known.Add)
                .Select(videoId => new IntakeRecord { VideoId = videoId, Title = videoId })
                .ToList();

            if (added.Count is not 0)
                Write([.. Read(), .. added]);
        }
    }

    /// <summary>
    /// Records that these entries have been offered back, so no scan offers them again.
    /// </summary>
    /// <remarks>
    /// Called before the removal, not after: the other order costs the whole playlist's quota on
    /// every scan after a crash.
    /// </remarks>
    public void MarkRemovalAttempted(IEnumerable<string> videoIds, DateTimeOffset at)
    {
        lock (_gate)
        {
            var marking = videoIds.ToHashSet(StringComparer.Ordinal);

            Write([
                .. Read()
                    .Select(record =>
                        marking.Contains(record.VideoId)
                            ? record with
                            {
                                RemovalAttemptedAt = at,
                            }
                            : record
                    ),
            ]);
        }
    }

    /// <summary>
    /// Drops records the backfill should never have made: ones standing in for an instruction
    /// that asks for nothing.
    /// </summary>
    /// <remarks>
    /// A backfilled record is known by its shape: the video ID for a title and no added-at. A
    /// record made from a real entry has both, so none is at risk.
    /// </remarks>
    public void Prune(IReadOnlySet<string> owed, IReadOnlySet<string> held)
    {
        lock (_gate)
        {
            var kept = Read()
                .Where(record =>
                    !IsStandIn(record)
                    || owed.Contains(record.VideoId)
                    || held.Contains(record.VideoId)
                )
                .ToList();

            if (kept.Count != Read().Count)
                Write(kept);
        }
    }

    private static bool IsStandIn(IntakeRecord record) =>
        record.AddedAt is null && record.Title.Equals(record.VideoId, StringComparison.Ordinal);

    /// <summary>Drops a record, which is what forgetting a track means.</summary>
    public void Forget(string videoId)
    {
        lock (_gate)
        {
            var kept = Read()
                .Where(r => !r.VideoId.Equals(videoId, StringComparison.Ordinal))
                .ToList();

            if (kept.Count != Read().Count)
                Write(kept);
        }
    }

    /// <summary>
    /// Everything crateside has taken on: what the playlist holds now, then what it held once.
    /// </summary>
    /// <remarks>
    /// An entry that is still there wins: it carries a live
    /// <see cref="QueueItem.PlaylistItemId"/>, which is what removal needs to delete it.
    /// </remarks>
    public static IReadOnlyList<QueueItem> Merge(
        IReadOnlyList<QueueItem> held,
        IEnumerable<IntakeRecord> recorded
    )
    {
        var current = held.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. held,
            .. recorded
                .Where(record => !current.Contains(record.VideoId))
                .Select(AsQueueItem)
                .OrderBy(item => item.AddedAt ?? DateTimeOffset.MaxValue),
        ];
    }

    /// <summary>The record read back as the queue item it stands in for.</summary>
    /// <remarks>
    /// No <see cref="QueueItem.PlaylistItemId"/>: there is no entry behind it any more, which is
    /// what tells removal it has nothing to delete.
    /// </remarks>
    public static QueueItem AsQueueItem(IntakeRecord record) =>
        new()
        {
            VideoId = record.VideoId,
            Title = record.Title,
            AddedAt = record.AddedAt,
            PremiumOutlook = record.PremiumOutlook,
            IsUnavailable = record.IsUnavailable,
        };

    private IReadOnlyList<IntakeRecord>? TryReadFile()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<IntakeRecord>>(
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

    private void Write(IReadOnlyList<IntakeRecord> records)
    {
        _cached = records;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(records, SerializerOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Kept in memory rather than failing the scan, but nothing may be consumed on the
            // strength of it: see IsDurable.
            _durable = false;
        }
    }
}

/// <summary>One track crateside has taken on, and what the playlist said about it.</summary>
public sealed record IntakeRecord
{
    [JsonPropertyName("videoId")]
    public required string VideoId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("addedAt")]
    public DateTimeOffset? AddedAt { get; init; }

    [JsonPropertyName("premiumOutlook")]
    public PremiumOutlook PremiumOutlook { get; init; }

    [JsonPropertyName("isUnavailable")]
    public bool IsUnavailable { get; init; }

    /// <summary>
    /// When the playlist entry was offered back, or null while it never has been.
    /// </summary>
    /// <remarks>
    /// Set whether the removal succeeded or not, and never cleared: a failed delete costs fifty
    /// quota units and must not be retried on the next scan.
    /// </remarks>
    [JsonPropertyName("removalAttemptedAt")]
    public DateTimeOffset? RemovalAttemptedAt { get; init; }
}
