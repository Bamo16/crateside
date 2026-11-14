using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Familiarity;

/// <summary>
/// Each song's listeners and Wikipedia pageviews and the familiarity the user settled, kept in the
/// ledger by song identity, as genre is (ADR 0089, 0082).
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

    /// <summary>Records what the sources said, leaving anything the user settled alone.</summary>
    public void Measure(string song, FamiliarityReading reading, DateTimeOffset at, int version) =>
        Update(
            song,
            record =>
                record with
                {
                    Listeners = reading.Listeners,
                    Pageviews = reading.Pageviews,
                    WikidataItem = reading.WikidataItem,
                    LookedUpAt = at,
                    LookupVersion = version,
                }
        );

    /// <summary>The user's answer, which outranks whatever the numbers say.</summary>
    public void Settle(string song, string value)
    {
        if (!FamiliarityVocabulary.IsValue(value))
            throw new ArgumentException($"'{value}' is not a familiarity.", nameof(value));

        Update(
            song,
            record =>
                record with
                {
                    Settled = value,
                    Rejected = [.. record.Rejected.Where(rejected => rejected != value)],
                }
        );
    }

    /// <summary>
    /// The person took the value off a file in Live: it is not written again until they settle it
    /// (ADR 0087).
    /// </summary>
    /// <param name="removal">Names the file's removal, which is not acted on twice.</param>
    public void Reject(string song, string value, string removal) =>
        Update(
            song,
            record =>
                record with
                {
                    Settled = record.Settled == value ? null : record.Settled,
                    Rejected = [.. record.Rejected.Union([value])],
                    RemovalsSeen = [.. record.RemovalsSeen.Union([removal])],
                }
        );

    /// <summary>Notes the value asked of each song's files, so a later change can take it off again.</summary>
    public void MarkTagged(IEnumerable<(string Song, string Value)> asked)
    {
        lock (_gate)
        {
            var records = _cached ??= Load();
            var changed = asked
                .Select(song => (Record: records.GetValueOrDefault(song.Song), song.Value))
                .Where(song => song.Record is not null && !song.Record.Tagged.Contains(song.Value))
                .Select(song => song.Record! with { Tagged = [.. song.Record.Tagged, song.Value] })
                .ToArray();

            if (changed.Length is 0)
                return;

            foreach (var record in changed)
                records[record.Song] = record;

            ledger.Save(context =>
            {
                foreach (var record in changed)
                    context.Upsert(
                        context.Familiarities,
                        row => row.Song == record.Song,
                        FamiliarityRow.From(record)
                    );
            });
        }
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
        (
            held switch
            {
                null => moved with { Song = song },
                { LookedUpAt: null } => moved with
                {
                    Song = song,
                    Settled = held.Settled ?? moved.Settled,
                },
                _ => held with { Settled = held.Settled ?? moved.Settled },
            }
        ) with
        {
            Tagged = [.. (held?.Tagged ?? []).Union(moved.Tagged)],
            Rejected = [.. (held?.Rejected ?? []).Union(moved.Rejected)],
            RemovalsSeen = [.. (held?.RemovalsSeen ?? []).Union(moved.RemovalsSeen)],
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

    /// <summary>Distinct ListenBrainz users of the song's best-played recording; null when it has none.</summary>
    public int? Listeners { get; init; }

    /// <summary>User pageviews of the song's English Wikipedia article over the last twelve months; 0 for no article.</summary>
    public int? Pageviews { get; init; }

    /// <summary>The Wikidata item the song's article was found through, or null when none matched.</summary>
    public string? WikidataItem { get; init; }

    /// <summary>When the sources were last asked, or null when they never have been.</summary>
    public DateTimeOffset? LookedUpAt { get; init; }

    /// <summary>Which <see cref="FamiliarityLookup.Version"/> asked; zero for the first.</summary>
    public int LookupVersion { get; init; }

    /// <summary>
    /// Worth asking again: never asked, or asked by a lookup older than the current one, whose
    /// matching may have missed the song.
    /// </summary>
    [JsonIgnore]
    public bool IsOwedALookup => LookedUpAt is null || LookupVersion < FamiliarityLookup.Version;

    /// <summary>The user's answer, or null when they have not given one.</summary>
    public string? Settled { get; init; }

    /// <summary>Every value crateside has asked to be written onto the song's files.</summary>
    public IReadOnlyList<string> Tagged { get; init; } = [];

    /// <summary>Values the person took off in Live, which are not written again until they settle them.</summary>
    public IReadOnlyList<string> Rejected { get; init; } = [];

    /// <summary>Each removal already acted on, per stem file, so a choice made since is not undone by the same removal.</summary>
    public IReadOnlyList<string> RemovalsSeen { get; init; } = [];

    /// <summary>What the numbers say, or null before the sources have been asked.</summary>
    [JsonIgnore]
    public string? Suggested =>
        LookedUpAt is null ? null : FamiliarityVocabulary.FromReach(Listeners, Pageviews);

    /// <summary>The value to tag with: the user's, else the numbers'; null before the first lookup, or when the person took it off.</summary>
    [JsonIgnore]
    public string? Written =>
        (Settled ?? Suggested) switch
        {
            { } value when !Rejected.Contains(value) => value,
            _ => null,
        };

    /// <summary>Values crateside asked to be written before that the song no longer has.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Retired => [.. Tagged.Where(value => value != Written)];
}

/// <summary>What the sources said about one song.</summary>
public sealed record FamiliarityReading(int? Listeners, int? Pageviews, string? WikidataItem);
