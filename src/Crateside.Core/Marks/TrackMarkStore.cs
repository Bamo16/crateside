using System.Text.Json.Serialization;
using Crateside.Core.Watching;

namespace Crateside.Core.Marks;

/// <summary>
/// The marks the maintainer has set, kept in <c>%APPDATA%\crateside\track-marks.json</c> by song
/// identity, as genre and familiarity are.
/// </summary>
public sealed class TrackMarkStore
{
    private readonly string _path;
    private readonly Lock _gate = new();

    private Dictionary<string, TrackMarkRecord>? _cached;

    /// <summary>Raised after a mark is set or cleared, with the song it was set on.</summary>
    public event Action<string>? Changed;

    /// <summary>A mark set or cleared counts as a change to the files the rows are built from.</summary>
    public TrackMarkStore(CratesidePaths paths, FileChanges changes)
        : this(paths.TrackMarksFile) => Changed += _ => changes.NotifyNow();

    internal TrackMarkStore(string path) => _path = path;

    /// <summary>Every song with a mark, by song identity. Never throws: an unreadable file reads as empty.</summary>
    public IReadOnlyDictionary<string, TrackMarkRecord> Read()
    {
        lock (_gate)
            return new Dictionary<string, TrackMarkRecord>(
                _cached ??= TryReadFile(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    /// <summary>The song's marks, none for a song never marked.</summary>
    public IReadOnlyList<TrackMark> Of(string song) => Read().GetValueOrDefault(song)?.Marks ?? [];

    public void Set(string song, TrackMark mark, bool marked)
    {
        lock (_gate)
        {
            var records = _cached ??= TryReadFile();
            var marks = (records.GetValueOrDefault(song)?.Marks ?? [])
                .Where(existing => existing != mark)
                .Concat(marked ? [mark] : [])
                .Order()
                .ToList();

            if (marks.Count is 0)
                records.Remove(song);
            else
                records[song] = new TrackMarkRecord { Song = song, Marks = marks };

            Write(records);
        }

        Changed?.Invoke(song);
    }

    /// <summary>
    /// Moves each record kept under a video ID onto the song the ledger has since judged it as,
    /// calling <paramref name="beforeWrite"/> with the file's path first; returns how many moved.
    /// </summary>
    public int FollowSongs(Func<string, string?> songFor, Action<string>? beforeWrite = null)
    {
        lock (_gate)
        {
            var records = new Dictionary<string, TrackMarkRecord>(
                _cached ??= TryReadFile(),
                StringComparer.OrdinalIgnoreCase
            );

            var moved = SongKeys.Follow(
                records,
                songFor,
                (song, record, held) =>
                    new TrackMarkRecord
                    {
                        Song = song,
                        Marks = [.. (held?.Marks ?? []).Union(record.Marks).Order()],
                    }
            );

            if (moved is 0)
                return 0;

            try
            {
                beforeWrite?.Invoke(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }

            _cached = records;
            Write(records);

            return moved;
        }
    }

    private void Write(Dictionary<string, TrackMarkRecord> records)
    {
        try
        {
            TrackMarkJsonContext.Default.ListTrackMarkRecord.WriteFile(
                _path,
                [.. records.Values.OrderBy(record => record.Song)]
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private Dictionary<string, TrackMarkRecord> TryReadFile() =>
        (TrackMarkJsonContext.Default.ListTrackMarkRecord.ReadFileOrDefault(_path) ?? [])
            .Where(record => record.Marks.Count > 0)
            .ToDictionary(record => record.Song, StringComparer.OrdinalIgnoreCase);
}

public sealed record TrackMarkRecord
{
    /// <summary>The ledger's song identity, or the upload's video ID for a song it never judged.</summary>
    public required string Song { get; init; }

    public required IReadOnlyList<TrackMark> Marks { get; init; }

    public bool Has(TrackMark mark) => Marks.Contains(mark);
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(List<TrackMarkRecord>))]
internal sealed partial class TrackMarkJsonContext : JsonSerializerContext { }
