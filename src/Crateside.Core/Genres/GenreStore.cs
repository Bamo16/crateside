using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Genres;

/// <summary>
/// Each song's genre as the user settled it and as the sources suggested it, kept in
/// <c>%APPDATA%\crateside\genres.json</c> by song identity, as the ledger keeps its key (ADR 0030).
/// </summary>
public sealed class GenreStore
{
    private readonly string _path;
    private readonly Lock _gate = new();

    private Dictionary<string, GenreRecord>? _cached;

    public GenreStore(string? path = null) =>
        _path =
            path
            ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "genres.json");

    /// <summary>Every record, by song identity. Never throws: an unreadable file reads as empty.</summary>
    public IReadOnlyDictionary<string, GenreRecord> Read()
    {
        lock (_gate)
            return new Dictionary<string, GenreRecord>(
                _cached ??= TryReadFile(),
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

    private void Update(string song, Func<GenreRecord, GenreRecord> change)
    {
        lock (_gate)
        {
            var records = _cached ??= TryReadFile();

            records[song] = change(
                records.GetValueOrDefault(song) ?? new GenreRecord { Song = song }
            );

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(
                    _path,
                    JsonSerializer.Serialize(
                        records.Values.OrderBy(r => r.Song).ToList(),
                        GenreJsonContext.Default.ListGenreRecord
                    )
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private Dictionary<string, GenreRecord> TryReadFile()
    {
        try
        {
            return File.Exists(_path)
                ? (
                    JsonSerializer.Deserialize(
                        File.ReadAllText(_path),
                        GenreJsonContext.Default.ListGenreRecord
                    ) ?? []
                ).ToDictionary(r => r.Song, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
            when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
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
