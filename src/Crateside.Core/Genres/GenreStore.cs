using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Genres;

/// <summary>
/// Each song's genre as the user settled it and as the sources suggested it, kept in the ledger
/// by song identity, as it keeps the key (ADR 0030, 0082).
/// </summary>
public sealed class GenreStore(LedgerStore ledger)
{
    private readonly Lock _gate = new();

    private Dictionary<string, GenreRecord>? _cached;

    /// <summary>Every record, by song identity.</summary>
    public IReadOnlyDictionary<string, GenreRecord> Read()
    {
        lock (_gate)
            return new Dictionary<string, GenreRecord>(
                _cached ??= Load(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    /// <summary>Records what the sources said, leaving anything the user settled alone.</summary>
    public void Suggest(string song, IReadOnlyList<string> heard, DateTimeOffset at) =>
        Update(
            song,
            record =>
                record with
                {
                    Heard = heard,
                    Suggested = heard
                        .Select(GenreVocabulary.FromHeard)
                        .OfType<string>()
                        .FirstOrDefault(),
                    LookedUpAt = at,
                }
        );

    /// <summary>The user's answer: a genre from the list, or null for none of them.</summary>
    public void Settle(string song, string? genre)
    {
        if (genre is not null && !GenreVocabulary.IsGenre(genre))
            throw new ArgumentException($"'{genre}' is not on the genre list.", nameof(genre));

        Update(song, record => record with { Genre = genre, IsSettled = true, LearnedFrom = null });
    }

    /// <summary>
    /// Settles a song the user has not answered from what their answers say about its artist; a
    /// song they answered themselves is left alone (#61).
    /// </summary>
    public void SettleFromArtist(string song, string genre, string artist)
    {
        if (!GenreVocabulary.IsGenre(genre))
            throw new ArgumentException($"'{genre}' is not on the genre list.", nameof(genre));

        Update(
            song,
            record =>
                record is { IsSettled: true, LearnedFrom: null }
                    ? record
                    : record with
                    {
                        Genre = genre,
                        IsSettled = true,
                        LearnedFrom = artist,
                    }
        );
    }

    /// <summary>Takes back a genre settled from the artist, once the user's answers stop supporting it.</summary>
    public void Unlearn(string song) =>
        Update(
            song,
            record =>
                record.LearnedFrom is null
                    ? record
                    : record with
                    {
                        Genre = null,
                        IsSettled = false,
                        LearnedFrom = null,
                    }
        );

    /// <summary>Moves each record kept under a video ID onto the song the ledger has since judged it as; returns how many moved.</summary>
    public int FollowSongs(Func<string, string?> songFor)
    {
        lock (_gate)
        {
            var before = _cached ??= Load();
            var records = new Dictionary<string, GenreRecord>(
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
                    context.Genres.RemoveRange(context.Genres.Where(row => row.Song == gone));

                foreach (
                    var record in records.Values.Where(record =>
                        !before.TryGetValue(record.Song, out var old) || old != record
                    )
                )
                    context.Upsert(
                        context.Genres,
                        row => row.Song == record.Song,
                        GenreRow.From(record)
                    );
            });

            return moved;
        }
    }

    /// <summary>The user's own answer wins wherever it was given; otherwise the song's record, the later one.</summary>
    private static GenreRecord Merged(string song, GenreRecord moved, GenreRecord? held)
    {
        if (held is null)
            return moved with { Song = song };

        var answer =
            held.IsTheUsersAnswer || !moved.IsTheUsersAnswer && held.IsSettled ? held : moved;
        var lookup = held.LookedUpAt is not null ? held : moved;

        return held with
        {
            Genre = answer.Genre,
            IsSettled = answer.IsSettled,
            LearnedFrom = answer.LearnedFrom,
            Heard = lookup.Heard,
            Suggested = lookup.Suggested,
            LookedUpAt = lookup.LookedUpAt,
        };
    }

    private void Update(string song, Func<GenreRecord, GenreRecord> change)
    {
        lock (_gate)
        {
            var records = _cached ??= Load();
            var changed = change(
                records.GetValueOrDefault(song) ?? new GenreRecord { Song = song }
            );

            records[song] = changed;
            ledger.Save(context =>
                context.Upsert(
                    context.Genres,
                    row => row.Song == changed.Song,
                    GenreRow.From(changed)
                )
            );
        }
    }

    private Dictionary<string, GenreRecord> Load() =>
        ledger
            .Query(context => context.Genres.AsNoTracking().ToList())
            .ToDictionary(row => row.Song, row => row.ToRecord(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>One song's genre, settled or suggested.</summary>
public sealed record GenreRecord
{
    /// <summary>The ledger's song identity, or the upload's video ID for a song it never judged.</summary>
    public required string Song { get; init; }

    /// <summary>What the user settled on, or null — which, once settled, means none of the list.</summary>
    public string? Genre { get; init; }

    public bool IsSettled { get; init; }

    /// <summary>
    /// The artist whose other tracks settled this one, or null when the user settled it or nobody has.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LearnedFrom { get; init; }

    /// <summary>The user answered this song themselves: the only answer that teaches anything.</summary>
    [JsonIgnore]
    public bool IsTheUsersAnswer => IsSettled && LearnedFrom is null;

    /// <summary>The first thing the sources said that maps onto the list, or null.</summary>
    public string? Suggested { get; init; }

    /// <summary>Everything the sources said, verbatim, whether it maps onto the list or not.</summary>
    public IReadOnlyList<string> Heard { get; init; } = [];

    /// <summary>When the sources were last asked, or null when they never have been.</summary>
    public DateTimeOffset? LookedUpAt { get; init; }

    /// <summary>The genre to write: only ever a settled one.</summary>
    [JsonIgnore]
    public string? Written => IsSettled ? Genre : null;
}
