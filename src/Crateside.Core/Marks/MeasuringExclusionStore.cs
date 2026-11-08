using Crateside.Core.Ledger;
using Crateside.Core.Watching;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Marks;

/// <summary>
/// The tracks, and stretches of tracks, marked as not fit for measuring detectors, kept in the
/// ledger by song identity as the other marks are (ADR 0085).
/// </summary>
public sealed class MeasuringExclusionStore
{
    private readonly LedgerStore _ledger;
    private readonly Lock _gate = new();

    private Dictionary<string, MeasuringExclusion>? _cached;

    /// <summary>Raised after a mark is set or cleared, with the song it was set on.</summary>
    public event Action<string>? Changed;

    /// <summary>A mark set or cleared counts as a change to the files the rows are built from.</summary>
    public MeasuringExclusionStore(LedgerStore ledger, FileChanges changes)
        : this(ledger) => Changed += _ => changes.NotifyNow();

    internal MeasuringExclusionStore(LedgerStore ledger) => _ledger = ledger;

    /// <summary>Every song with an exclusion, by song identity.</summary>
    public IReadOnlyDictionary<string, MeasuringExclusion> Read()
    {
        lock (_gate)
            return new Dictionary<string, MeasuringExclusion>(
                _cached ??= Load(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    public MeasuringExclusion? Of(string song) => Read().GetValueOrDefault(song);

    /// <summary>Marks the whole track, which replaces any stretches it had.</summary>
    public void MarkWholeTrack(string song, string reason) =>
        Change(song, held => new MeasuringExclusion { Song = held?.Song ?? song, Reason = reason });

    /// <summary>Adds a stretch; a whole-track mark gives way to it, and stretches stay in order of start.</summary>
    public void AddStretch(string song, MeasuringStretch stretch) =>
        Change(
            song,
            held => new MeasuringExclusion
            {
                Song = held?.Song ?? song,
                Stretches = Ordered((held?.Stretches ?? []).Append(stretch)),
            }
        );

    public void RemoveStretch(string song, MeasuringStretch stretch) =>
        Change(
            song,
            held =>
                held is null
                    ? null
                    : held with
                    {
                        Stretches = [.. held.Stretches.Where(each => each != stretch)],
                    }
        );

    public void Clear(string song) => Change(song, _ => null);

    /// <summary>Moves each record kept under a video ID onto the song the ledger has since judged it as; returns how many moved.</summary>
    public int FollowSongs(Func<string, string?> songFor)
    {
        lock (_gate)
        {
            var before = _cached ??= Load();
            var records = new Dictionary<string, MeasuringExclusion>(
                before,
                StringComparer.OrdinalIgnoreCase
            );

            var moved = SongKeys.Follow(
                records,
                songFor,
                (song, record, held) =>
                    record.WholeTrack || held?.WholeTrack is true
                        ? new MeasuringExclusion
                        {
                            Song = song,
                            Reason = held?.Reason ?? record.Reason,
                        }
                        : new MeasuringExclusion
                        {
                            Song = song,
                            Stretches = Ordered((held?.Stretches ?? []).Union(record.Stretches)),
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

    private static MeasuringStretch[] Ordered(IEnumerable<MeasuringStretch> stretches) =>
        [.. stretches.OrderBy(each => each.StartSeconds).ThenBy(each => each.EndSeconds)];

    private void Change(string song, Func<MeasuringExclusion?, MeasuringExclusion?> change)
    {
        lock (_gate)
        {
            var records = _cached ??= Load();
            var held = records.GetValueOrDefault(song);

            if (change(held) is { WholeTrack: true } or { Stretches: [_, ..] })
                records[song] = change(held)!;
            else
                records.Remove(song);

            Persist([held?.Song ?? song], records);
        }

        Changed?.Invoke(song);
    }

    private void Persist(
        IEnumerable<string> songs,
        Dictionary<string, MeasuringExclusion> records
    ) =>
        _ledger.Save(context =>
        {
            foreach (var song in songs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                context.MeasuringExclusions.RemoveRange(
                    context.MeasuringExclusions.Where(row => row.Song == song)
                );

                if (records.GetValueOrDefault(song) is { } record)
                    context.MeasuringExclusions.AddRange(MeasuringExclusionRow.From(record));
            }
        });

    private Dictionary<string, MeasuringExclusion> Load() =>
        _ledger
            .Query(context => context.MeasuringExclusions.AsNoTracking().ToList())
            .GroupBy(row => row.Song, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                song => song.Key,
                song => new MeasuringExclusion
                {
                    Song = song.Key,
                    Reason = song.FirstOrDefault(row => row.StartSeconds is null)?.Reason,
                    Stretches = Ordered(
                        song.Select(row => row.ToStretch()).OfType<MeasuringStretch>()
                    ),
                },
                StringComparer.OrdinalIgnoreCase
            );
}
