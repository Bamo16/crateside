using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.Watching;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Marks;

/// <summary>
/// The marks the maintainer has set, kept in the ledger by song identity, as genre and
/// familiarity are (ADR 0082).
/// </summary>
public sealed class TrackMarkStore
{
    private readonly LedgerStore _ledger;
    private readonly Lock _gate = new();

    private Dictionary<string, TrackMarkRecord>? _cached;

    /// <summary>Raised after a mark is set or cleared, with the song it was set on.</summary>
    public event Action<string>? Changed;

    /// <summary>A mark set or cleared counts as a change to the files the rows are built from.</summary>
    public TrackMarkStore(LedgerStore ledger, FileChanges changes)
        : this(ledger) => Changed += _ => changes.NotifyNow();

    internal TrackMarkStore(LedgerStore ledger) => _ledger = ledger;

    /// <summary>Every song with a mark, by song identity.</summary>
    public IReadOnlyDictionary<string, TrackMarkRecord> Read()
    {
        lock (_gate)
            return new Dictionary<string, TrackMarkRecord>(
                _cached ??= Load(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    /// <summary>The song's marks, none for a song never marked.</summary>
    public IReadOnlyList<TrackMark> Of(string song) => Read().GetValueOrDefault(song)?.Marks ?? [];

    public void Set(string song, TrackMark mark, bool marked)
    {
        lock (_gate)
        {
            var records = _cached ??= Load();
            var held = records.GetValueOrDefault(song);
            var marks = (held?.Marks ?? [])
                .Where(existing => existing != mark)
                .Concat(marked ? [mark] : [])
                .Order()
                .ToList();

            if (marks.Count is 0)
                records.Remove(song);
            else
                records[song] = new TrackMarkRecord { Song = held?.Song ?? song, Marks = marks };

            Persist([held?.Song ?? song], records);
        }

        Changed?.Invoke(song);
    }

    /// <summary>Moves each record kept under a video ID onto the song the ledger has since judged it as; returns how many moved.</summary>
    public int FollowSongs(Func<string, string?> songFor)
    {
        lock (_gate)
        {
            var before = _cached ??= Load();
            var records = new Dictionary<string, TrackMarkRecord>(
                before,
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

            var changed = before
                .Values.Select(record => record.Song)
                .Concat(records.Values.Select(record => record.Song))
                .ToList();

            _cached = records;
            Persist(changed, records);

            return moved;
        }
    }

    private void Persist(IEnumerable<string> songs, Dictionary<string, TrackMarkRecord> records) =>
        _ledger.Save(context =>
        {
            foreach (var song in songs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                context.TrackMarks.RemoveRange(context.TrackMarks.Where(row => row.Song == song));
                context.TrackMarks.AddRange(
                    (records.GetValueOrDefault(song)?.Marks ?? []).Select(mark => new TrackMarkRow
                    {
                        Song = records[song].Song,
                        Mark = mark,
                    })
                );
            }
        });

    private Dictionary<string, TrackMarkRecord> Load() =>
        _ledger
            .Query(context => context.TrackMarks.AsNoTracking().ToList())
            .GroupBy(row => row.Song, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                song => song.Key,
                song => new TrackMarkRecord
                {
                    Song = song.Key,
                    Marks = [.. song.Select(row => row.Mark).Order()],
                },
                StringComparer.OrdinalIgnoreCase
            );
}

/// <summary>One song's marks, as the store files them.</summary>
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
