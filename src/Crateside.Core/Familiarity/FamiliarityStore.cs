using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Familiarity;

/// <summary>
/// Each song's reach as Deezer measured it and the familiarity the user settled, kept in the
/// ledger by song identity, as genre is (#52, ADR 0082).
/// </summary>
public sealed class FamiliarityStore(LedgerStore ledger)
{
    private readonly Lock _gate = new();

    private Dictionary<string, FamiliarityRecord>? _cached;

    /// <summary>Every record, by song identity.</summary>
    public IReadOnlyDictionary<string, FamiliarityRecord> Read()
    {
        lock (_gate)
            return new Dictionary<string, FamiliarityRecord>(
                _cached ??= Load(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    /// <summary>Records what Deezer said, leaving anything the user settled alone.</summary>
    public void Measure(string song, FamiliarityReading reading, DateTimeOffset at, int version) =>
        Update(
            song,
            record =>
                record with
                {
                    Artist = reading.Artist,
                    ArtistFans = reading.ArtistFans,
                    SongRank = reading.SongRank,
                    LookedUpAt = at,
                    LookupVersion = version,
                }
        );

    /// <summary>The user's answer, which outranks whatever the numbers say.</summary>
    public void Settle(string song, string value)
    {
        if (!FamiliarityVocabulary.IsValue(value))
            throw new ArgumentException($"'{value}' is not a familiarity.", nameof(value));

        Update(song, record => record with { Settled = value });
    }

    /// <summary>Moves each record kept under a video ID onto the song the ledger has since judged it as; returns how many moved.</summary>
    public int FollowSongs(Func<string, string?> songFor)
    {
        lock (_gate)
        {
            var before = _cached ??= Load();
            var records = new Dictionary<string, FamiliarityRecord>(
                before,
                StringComparer.OrdinalIgnoreCase
            );

            var moved = SongKeys.Follow(records, songFor, Merged);

            if (moved is 0)
                return 0;

            _cached = records;
            ledger.Save(context =>
            {
                foreach (var gone in before.Keys.Where(song => !records.ContainsKey(song)))
                    context.Familiarities.RemoveRange(
                        context.Familiarities.Where(row => row.Song == gone)
                    );

                foreach (
                    var record in records.Values.Where(record =>
                        !before.TryGetValue(record.Song, out var old) || old != record
                    )
                )
                    context.Upsert(
                        context.Familiarities,
                        row => row.Song == record.Song,
                        FamiliarityRow.From(record)
                    );
            });

            return moved;
        }
    }

    /// <summary>The song's measurement where it has one, and the user's answer wherever it was given.</summary>
    private static FamiliarityRecord Merged(
        string song,
        FamiliarityRecord moved,
        FamiliarityRecord? held
    ) =>
        held switch
        {
            null => moved with { Song = song },
            { LookedUpAt: null } => moved with
            {
                Song = song,
                Settled = held.Settled ?? moved.Settled,
            },
            _ => held with { Settled = held.Settled ?? moved.Settled },
        };

    private void Update(string song, Func<FamiliarityRecord, FamiliarityRecord> change)
    {
        lock (_gate)
        {
            var records = _cached ??= Load();
            var changed = change(
                records.GetValueOrDefault(song) ?? new FamiliarityRecord { Song = song }
            );

            records[song] = changed;
            ledger.Save(context =>
                context.Upsert(
                    context.Familiarities,
                    row => row.Song == changed.Song,
                    FamiliarityRow.From(changed)
                )
            );
        }
    }

    private Dictionary<string, FamiliarityRecord> Load() =>
        ledger
            .Query(context => context.Familiarities.AsNoTracking().ToList())
            .ToDictionary(row => row.Song, row => row.ToRecord(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>One song's familiarity: measured, settled, or both.</summary>
public sealed record FamiliarityRecord
{
    /// <summary>The ledger's song identity, or the upload's video ID for a song it never judged.</summary>
    public required string Song { get; init; }

    /// <summary>The most-followed artist credited on the song Deezer matched.</summary>
    public string? Artist { get; init; }

    public long? ArtistFans { get; init; }

    /// <summary>Deezer's popularity figure for the song itself, 0 to about 1,000,000.</summary>
    public int? SongRank { get; init; }

    /// <summary>When Deezer was last asked, or null when it never has been.</summary>
    public DateTimeOffset? LookedUpAt { get; init; }

    /// <summary>Which <see cref="FamiliarityLookup.Version"/> asked; zero for the first.</summary>
    public int LookupVersion { get; init; }

    /// <summary>
    /// Worth asking again: never asked, or asked by a lookup older than the current one, whose
    /// matching may have taken a cover for the song.
    /// </summary>
    [JsonIgnore]
    public bool IsOwedALookup => LookedUpAt is null || LookupVersion < FamiliarityLookup.Version;

    /// <summary>The user's answer, or null when they have not given one.</summary>
    public string? Settled { get; init; }

    /// <summary>What the numbers settle on their own, or null for the band that asks the user.</summary>
    [JsonIgnore]
    public string? Suggested =>
        LookedUpAt is null ? null : FamiliarityVocabulary.FromReach(ArtistFans, SongRank);

    /// <summary>The value to tag with: the user's, else one the numbers settled; null means ask.</summary>
    [JsonIgnore]
    public string? Written => Settled ?? Suggested;
}

/// <summary>What Deezer said about one song, or all nulls when it did not know it.</summary>
public sealed record FamiliarityReading(string? Artist, long? ArtistFans, int? SongRank)
{
    public static FamiliarityReading Unknown { get; } = new(null, null, null);
}
