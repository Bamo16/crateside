using System.Text.Json.Serialization;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Playlist;

/// <summary>
/// What crateside keeps in <c>%APPDATA%\crateside\intake.json</c> about a playlist entry, so it
/// never has to read the entry again (ADR 0021).
/// </summary>
public sealed class IntakeStore(CratesidePaths paths)
{
    private readonly string _path = paths.IntakeFile;
    private readonly Lock _gate = new();

    internal IntakeStore(string path)
        : this(new CratesidePaths { IntakeFile = path }) { }

    private IReadOnlyList<IntakeRecord>? _cached;
    private bool _durable = true;

    /// <summary>
    /// Whether everything recorded has reached disk — false for the rest of the run once a write
    /// has failed.
    /// </summary>
    public bool IsDurable
    {
        get
        {
            lock (_gate)
                return _durable;
        }
    }

    /// <summary>
    /// Every track crateside has taken on. Never throws — an unreadable file reads as empty,
    /// which costs a re-read of the playlist rather than losing anything.
    /// </summary>
    public IReadOnlyList<IntakeRecord> Read()
    {
        lock (_gate)
            return _cached ??= TryReadFile() ?? [];
    }

    /// <summary>Entries crateside tried to empty from the playlist and could not.</summary>
    public IReadOnlyList<IntakeRecord> LeftInPlaylist =>
        [.. Read().Where(record => record.RemovalFailure is not null)];

    /// <summary>
    /// Remembers every track it has not seen before, and returns the ones it added. Existing
    /// records are never refreshed.
    /// </summary>
    public IReadOnlyList<IntakeRecord> Record(IEnumerable<WorkItem> items)
    {
        lock (_gate)
        {
            var known = Read().Select(r => r.TrackKey).ToHashSet(StringComparer.Ordinal);
            var added = items
                .Where(item => known.Add(item.TrackKey))
                .Select(item => new IntakeRecord
                {
                    TrackKey = item.TrackKey,
                    Title = item.Title,
                    ChannelTitle = item.ChannelTitle,
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
    /// The entries that are new arrivals: tracks never taken on, and tracks whose entry crateside
    /// removed that the user has added again (#84). Records the first and puts the second back in
    /// line to be removed.
    /// </summary>
    public IReadOnlyList<WorkItem> Arrive(IReadOnlyList<WorkItem> held)
    {
        lock (_gate)
        {
            var removed = Read()
                .Where(record => record is { RemovalAttemptedAt: not null, RemovalFailure: null })
                .ToDictionary(record => record.TrackKey, StringComparer.Ordinal);

            // A new entry has a new ID; the one crateside removed, read again from a stale
            // playlist, does not.
            var again = held.Where(item =>
                    item.PlaylistItemId is { } entry
                    && removed.TryGetValue(item.TrackKey, out var record)
                    && entry != record.RemovedEntryId
                )
                .ToList();

            if (again.Count is not 0)
                Update(
                    again.Select(item => item.TrackKey),
                    record => record with { RemovalAttemptedAt = null, RemovedEntryId = null }
                );

            var known = Read().Select(record => record.TrackKey).ToHashSet(StringComparer.Ordinal);
            var fresh = held.Where(item => !known.Contains(item.TrackKey)).ToList();

            Record(fresh);

            return [.. fresh, .. again];
        }
    }

    /// <summary>
    /// Takes tracks crateside already knows of into the intake, for a store that predates it.
    /// </summary>
    public void Backfill(IEnumerable<string> trackKeys)
    {
        lock (_gate)
        {
            var known = Read().Select(r => r.TrackKey).ToHashSet(StringComparer.Ordinal);
            var added = trackKeys
                .Where(known.Add)
                .Select(trackKey => new IntakeRecord { TrackKey = trackKey, Title = trackKey })
                .ToList();

            if (added.Count is not 0)
                Write([.. Read(), .. added]);
        }
    }

    /// <summary>
    /// Records that these entries have been offered back, so no scan offers them again. Call it
    /// before the removal, never after.
    /// </summary>
    /// <param name="entries">The playlist entry offered for each video, so a later re-add is told from a stale read.</param>
    public void MarkRemovalAttempted(
        IEnumerable<string> trackKeys,
        DateTimeOffset at,
        IReadOnlyDictionary<string, string>? entries = null
    ) =>
        Update(
            trackKeys,
            record =>
                record with
                {
                    RemovalAttemptedAt = at,
                    RemovedEntryId = entries?.GetValueOrDefault(record.TrackKey),
                }
        );

    /// <summary>
    /// Marks and returns the held entries never offered back, in one step, so scans running at
    /// once never offer the same entry. Call it before the removal, never after.
    /// </summary>
    public IReadOnlyList<WorkItem> ClaimForRemoval(IReadOnlyList<WorkItem> held, DateTimeOffset at)
    {
        lock (_gate)
        {
            var offerable = Read()
                .Where(record => record.RemovalAttemptedAt is null)
                .Select(record => record.TrackKey)
                .ToHashSet(StringComparer.Ordinal);

            List<WorkItem> claimed =
            [
                .. held.Where(item =>
                    item.PlaylistItemId is not null && offerable.Contains(item.TrackKey)
                ),
            ];

            if (claimed.Count is not 0)
                MarkRemovalAttempted(
                    claimed.Select(item => item.TrackKey),
                    at,
                    claimed
                        .DistinctBy(item => item.TrackKey)
                        .ToDictionary(
                            item => item.TrackKey,
                            item => item.PlaylistItemId!,
                            StringComparer.Ordinal
                        )
                );

            return claimed;
        }
    }

    /// <summary>
    /// Puts these entries back in line to be offered — only for a removal that never reached the
    /// API and so spent no quota (#34).
    /// </summary>
    public void ClearRemovalAttempted(IEnumerable<string> trackKeys) =>
        Update(
            trackKeys,
            record =>
                record with
                {
                    RemovalAttemptedAt = null,
                    RemovedEntryId = null,
                    RemovalFailure = null,
                }
        );

    /// <summary>Keeps why each of these entries was left in the playlist.</summary>
    public void RecordRemovalFailures(IReadOnlyDictionary<string, RemovalFailure> byTrackKey) =>
        Update(
            byTrackKey.Keys,
            record => record with { RemovalFailure = byTrackKey[record.TrackKey] }
        );

    /// <summary>Forgets why these entries were left, once they are gone from the playlist however they went.</summary>
    public void ForgetRemovalFailures(IEnumerable<string> trackKeys) =>
        Update(trackKeys, record => record with { RemovalFailure = null });

    private void Update(IEnumerable<string> trackKeys, Func<IntakeRecord, IntakeRecord> change)
    {
        lock (_gate)
        {
            var changing = trackKeys.ToHashSet(StringComparer.Ordinal);

            Write([
                .. Read()
                    .Select(record => changing.Contains(record.TrackKey) ? change(record) : record),
            ]);
        }
    }

    /// <summary>
    /// Drops records the backfill should never have made: ones standing in for an instruction
    /// that asks for nothing.
    /// </summary>
    public void Prune(IReadOnlySet<string> owed, IReadOnlySet<string> held)
    {
        lock (_gate)
        {
            var kept = Read()
                .Where(record =>
                    !IsStandIn(record)
                    || owed.Contains(record.TrackKey)
                    || held.Contains(record.TrackKey)
                )
                .ToList();

            if (kept.Count != Read().Count)
                Write(kept);
        }
    }

    private static bool IsStandIn(IntakeRecord record) =>
        record.AddedAt is null && record.Title.Equals(record.TrackKey, StringComparison.Ordinal);

    /// <summary>Drops a record, which is what forgetting a track means.</summary>
    public void Forget(string trackKey)
    {
        lock (_gate)
        {
            var kept = Read()
                .Where(r => !r.TrackKey.Equals(trackKey, StringComparison.Ordinal))
                .ToList();

            if (kept.Count != Read().Count)
                Write(kept);
        }
    }

    /// <summary>
    /// Everything crateside has taken on: what the playlist holds now, which wins because removal
    /// needs its live entry, then what it held once.
    /// </summary>
    public static IReadOnlyList<WorkItem> Merge(
        IReadOnlyList<WorkItem> held,
        IEnumerable<IntakeRecord> recorded
    )
    {
        var current = held.Select(item => item.TrackKey).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. held,
            .. recorded
                .Where(record => !current.Contains(record.TrackKey))
                .Select(AsQueueItem)
                .OrderBy(item => item.AddedAt ?? DateTimeOffset.MaxValue),
        ];
    }

    /// <summary>
    /// The record read back as the queue item it stands in for, with no playlist entry to delete.
    /// </summary>
    public static WorkItem AsQueueItem(IntakeRecord record) =>
        new()
        {
            TrackKey = record.TrackKey,
            Title = record.Title,
            ChannelTitle = record.ChannelTitle,
            AddedAt = record.AddedAt,
            PremiumOutlook = record.PremiumOutlook,
            IsUnavailable = record.IsUnavailable,
        };

    private IReadOnlyList<IntakeRecord>? TryReadFile() =>
        IntakeJsonContext.Default.IReadOnlyListIntakeRecord.ReadFileOrDefault(_path);

    private void Write(IReadOnlyList<IntakeRecord> records)
    {
        _cached = records;

        try
        {
            IntakeJsonContext.Default.IReadOnlyListIntakeRecord.WriteFile(_path, records);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
    public required string TrackKey { get; init; }

    public required string Title { get; init; }

    /// <summary>The uploading channel, kept once the playlist entry is gone. Null on older records.</summary>
    public string? ChannelTitle { get; init; }

    public DateTimeOffset? AddedAt { get; init; }

    public PremiumOutlook PremiumOutlook { get; init; }

    public bool IsUnavailable { get; init; }

    /// <summary>
    /// When the playlist entry was offered back, whether or not the removal worked, or null while
    /// it never has been.
    /// </summary>
    public DateTimeOffset? RemovalAttemptedAt { get; init; }

    /// <summary>Why the removal failed, or null when it did not (#34).</summary>
    public RemovalFailure? RemovalFailure { get; init; }

    /// <summary>The playlist entry crateside offered back. Null on records older than #84.</summary>
    public string? RemovedEntryId { get; init; }
}

/// <summary>Why a playlist entry was left behind, kept because nothing retries it on its own.</summary>
public sealed record RemovalFailure
{
    public required string Reason { get; init; }

    public bool CanSucceedLater { get; init; }
}
