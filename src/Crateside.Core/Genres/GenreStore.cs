using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Genres;

/// <summary>
/// Each song's genres as the sources said them, as the person chose them and as learned from the
/// artist, kept in the ledger by song identity, as it keeps the key (ADR 0088, 0082).
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

    /// <summary>Records what the sources said, leaving the person's genres and a learned one alone.</summary>
    public void Hear(string song, GenreHearing hearing, DateTimeOffset at) =>
        Update(
            song,
            record =>
                record with
                {
                    Heard = hearing.Labels,
                    RecordingMbid = hearing.RecordingMbid ?? record.RecordingMbid,
                    LookedUpAt = at,
                }
        );

    /// <summary>
    /// The person's genre: one of the 29 replaces the one they chose before, one of their own is
    /// added to the rest.
    /// </summary>
    public void Choose(string song, string genre)
    {
        if (!GenreVocabulary.IsName(genre))
            throw new ArgumentException($"'{genre}' cannot be a genre.", nameof(genre));

        var isListed = GenreVocabulary.IsGenre(genre);

        Update(
            song,
            record =>
                record with
                {
                    Mine =
                    [
                        .. record
                            .Mine.Where(mine => !isListed || !GenreVocabulary.IsGenre(mine))
                            .Where(mine => mine != genre),
                        genre,
                    ],
                    Learned = isListed ? null : record.Learned,
                    LearnedFrom = isListed ? null : record.LearnedFrom,
                    Rejected = [.. record.Rejected.Where(rejected => rejected != genre)],
                }
        );
    }

    /// <summary>
    /// The person took the genre off a file in Live: it stops being the song's, and is not assigned
    /// again until they choose it (ADR 0087).
    /// </summary>
    /// <param name="removal">Names the file's removal, which is not acted on twice.</param>
    public void Reject(string song, string genre, string removal) =>
        Update(
            song,
            record =>
                record with
                {
                    Mine = [.. record.Mine.Where(mine => mine != genre)],
                    Rejected = [.. record.Rejected.Union([genre])],
                    RemovalsSeen = [.. record.RemovalsSeen.Union([removal])],
                }
        );

    /// <summary>
    /// Gives a song the person has not answered what their answers say about its artist; a song
    /// they answered themselves is left alone (#61).
    /// </summary>
    public void Learn(string song, string genre, string artist)
    {
        if (!GenreVocabulary.IsGenre(genre))
            throw new ArgumentException(
                $"'{genre}' is not one of Ableton's genres.",
                nameof(genre)
            );

        Update(
            song,
            record =>
                record.IsTheUsersAnswer
                    ? record
                    : record with
                    {
                        Learned = genre,
                        LearnedFrom = artist,
                    }
        );
    }

    /// <summary>Takes back a genre learned from the artist, once the person's answers stop supporting it.</summary>
    public void Unlearn(string song) =>
        Update(
            song,
            record =>
                record.Learned is null && record.LearnedFrom is null
                    ? record
                    : record with
                    {
                        Learned = null,
                        LearnedFrom = null,
                    }
        );

    /// <summary>Notes the genres asked of each song's files, so a later change can take them off again.</summary>
    public void MarkTagged(IEnumerable<(string Song, IReadOnlyList<string> Genres)> asked)
    {
        lock (_gate)
        {
            var records = _cached ??= Load();
            var changed = asked
                .Select(song => (Record: records.GetValueOrDefault(song.Song), song.Genres))
                .Where(song =>
                    song.Record is not null && song.Genres.Except(song.Record.Tagged).Any()
                )
                .Select(song =>
                    song.Record! with
                    {
                        Tagged = [.. song.Record.Tagged, .. song.Genres.Except(song.Record.Tagged)],
                    }
                )
                .ToArray();

            if (changed.Length is 0)
                return;

            foreach (var record in changed)
                records[record.Song] = record;

            ledger.Save(context =>
            {
                foreach (var record in changed)
                    context.Upsert(
                        context.Genres,
                        row => row.Song == record.Song,
                        GenreRow.From(record)
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

    /// <summary>The person's genres are kept wherever they were given; the later lookup stands.</summary>
    private static GenreRecord Merged(string song, GenreRecord moved, GenreRecord? held)
    {
        if (held is null)
            return moved with { Song = song };

        var lookup = held.LookedUpAt is not null ? held : moved;
        var learned = held.Learned is not null ? held : moved;

        return held with
        {
            Heard = lookup.Heard,
            RecordingMbid = held.RecordingMbid ?? moved.RecordingMbid,
            LookedUpAt = lookup.LookedUpAt,
            Learned = learned.Learned,
            LearnedFrom = learned.LearnedFrom,
            Mine =
            [
                .. (held.IsTheUsersAnswer ? held : moved).Mine.Where(GenreVocabulary.IsGenre),
                .. held.Mine.Union(moved.Mine).Where(mine => !GenreVocabulary.IsGenre(mine)),
            ],
            Tagged = [.. held.Tagged.Union(moved.Tagged)],
            Rejected = [.. held.Rejected.Union(moved.Rejected)],
            RemovalsSeen = [.. held.RemovalsSeen.Union(moved.RemovalsSeen)],
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

/// <summary>One song's genres: what the sources said, what crateside took from it, and what the person chose.</summary>
public sealed record GenreRecord
{
    /// <summary>The ledger's song identity, or the upload's video ID for a song it never judged.</summary>
    public required string Song { get; init; }

    /// <summary>Every label the sources gave, verbatim, in the lookup's order: MusicBrainz recording, MusicBrainz artist, Deezer, iTunes.</summary>
    public IReadOnlyList<string> Heard { get; init; } = [];

    /// <summary>The MusicBrainz recording the lookup matched, or null when none matched.</summary>
    public string? RecordingMbid { get; init; }

    /// <summary>When the sources were last asked, or null when they never have been.</summary>
    public DateTimeOffset? LookedUpAt { get; init; }

    /// <summary>A genre taken from the person's answers for the artist, used only where the sources map onto nothing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Learned { get; init; }

    /// <summary>The artist whose answers the learned genre came from.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LearnedFrom { get; init; }

    /// <summary>The genres the person chose: among the 29, or of their own.</summary>
    public IReadOnlyList<string> Mine { get; init; } = [];

    /// <summary>Every genre crateside has asked to be written onto the song's files.</summary>
    public IReadOnlyList<string> Tagged { get; init; } = [];

    /// <summary>Genres the person took off in Live, which are not assigned again until they choose them.</summary>
    public IReadOnlyList<string> Rejected { get; init; } = [];

    /// <summary>Each removal already acted on, per stem file, so a choice made since is not undone by the same removal.</summary>
    public IReadOnlyList<string> RemovalsSeen { get; init; } = [];

    /// <summary>The sources' labels, kept as the song's styles and never written as tags.</summary>
    public IReadOnlyList<string> Styles => Heard;

    /// <summary>What the sources' labels map onto, at most two of the 29.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> FromSources => GenreMapping.Pick(Heard);

    /// <summary>What crateside assigns: the sources' genres, else the one learned from the artist.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Assigned =>
        FromSources is [_, ..] sources ? sources
        : Learned is { } learned ? [learned]
        : [];

    /// <summary>The person chose one of the 29, which replaces crateside's and is the only answer that teaches.</summary>
    [JsonIgnore]
    public bool IsTheUsersAnswer => Mine.Any(GenreVocabulary.IsGenre);

    /// <summary>The genres to write: the person's choice among the 29, else crateside's, and always the person's own; never one they took off.</summary>
    public IReadOnlyList<string> Genres =>
        [
            .. (IsTheUsersAnswer ? Mine.Where(GenreVocabulary.IsGenre) : Assigned)
                .Concat(Mine.Where(mine => !GenreVocabulary.IsGenre(mine)))
                .Where(genre => !Rejected.Contains(genre)),
        ];

    /// <summary>Genres crateside asked to be written before that the song no longer has.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Retired => [.. Tagged.Except(Genres)];
}

/// <summary>What one lookup found: the labels of every source, and the recording MusicBrainz matched.</summary>
public sealed record GenreHearing(IReadOnlyList<string> Labels, string? RecordingMbid);
