using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Familiarity;

/// <summary>
/// Each song's reach as Deezer measured it and the familiarity the user settled, kept in
/// <c>%APPDATA%\crateside\familiarity.json</c> by song identity, as genre is (#52).
/// </summary>
public sealed class FamiliarityStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    private Dictionary<string, FamiliarityRecord>? _cached;

    public FamiliarityStore(string? path = null) =>
        _path =
            path
            ?? Environment.SpecialFolder.ApplicationData.GetFolderPath(
                "crateside",
                "familiarity.json"
            );

    /// <summary>Every record, by song identity. Never throws: an unreadable file reads as empty.</summary>
    public IReadOnlyDictionary<string, FamiliarityRecord> Read()
    {
        lock (_gate)
            return new Dictionary<string, FamiliarityRecord>(
                _cached ??= TryReadFile(),
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

    private void Update(string song, Func<FamiliarityRecord, FamiliarityRecord> change)
    {
        lock (_gate)
        {
            var records = _cached ??= TryReadFile();

            records[song] = change(
                records.GetValueOrDefault(song) ?? new FamiliarityRecord { Song = song }
            );

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(
                    _path,
                    JsonSerializer.Serialize(records.Values.OrderBy(r => r.Song), SerializerOptions)
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private Dictionary<string, FamiliarityRecord> TryReadFile()
    {
        try
        {
            return File.Exists(_path)
                ? (
                    JsonSerializer.Deserialize<List<FamiliarityRecord>>(
                        File.ReadAllText(_path),
                        SerializerOptions
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
    /// Worth asking again: never asked, or not found by a lookup older than the current one.
    /// </summary>
    [JsonIgnore]
    public bool IsOwedALookup =>
        LookedUpAt is null || (SongRank is null && LookupVersion < FamiliarityLookup.Version);

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
