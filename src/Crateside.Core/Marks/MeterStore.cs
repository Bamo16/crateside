using System.Text.Json.Serialization;
using Crateside.Core.Ableton;

namespace Crateside.Core.Marks;

/// <summary>
/// The Meter tags the maintainer sets in Live (#154): the set crateside last applied to each song's
/// stems, kept in <c>%APPDATA%\crateside\meters.json</c> by song identity.
/// </summary>
public sealed class MeterStore(CratesidePaths paths)
{
    private readonly string _path = paths.MetersFile;

    private readonly Lock _gate = new();

    internal MeterStore(string path)
        : this(new CratesidePaths { MetersFile = path }) { }

    /// <summary>Never throws: an unreadable file reads as no song having a Meter.</summary>
    public IReadOnlyDictionary<string, MeterRecord> Read()
    {
        lock (_gate)
            return Load();
    }

    public MeterRecord? Of(string song) => Read().GetValueOrDefault(song);

    /// <summary>The Meter keywords every stem of the song should carry; none while the stems contradict.</summary>
    public IReadOnlyList<string> Wanted(string song) =>
        Of(song) is { Contradicts: false } record
            ? [.. record.Applied.Select(MeterTags.Keyword)]
            : [];

    /// <summary>The Meter keywords crateside applied and no longer wants.</summary>
    public IReadOnlyList<string> Retired(string song) =>
        Of(song) is { Contradicts: false } record
            ? [.. record.Retired.Select(MeterTags.Keyword)]
            : [];

    /// <summary>
    /// The song's marks with Odd bars following the Meter tags once it has any: on for anything but
    /// plain 4/4 or a contradiction, off for plain 4/4, whatever the hand mark says.
    /// </summary>
    public IReadOnlyCollection<TrackMark> MarksFor(
        string song,
        IReadOnlyCollection<TrackMark> hand
    ) =>
        Of(song) is not { } record || record is { Applied: [], Contradicts: false }
            ? hand
            :
            [
                .. hand.Where(mark => mark is not TrackMark.OddBars),
                .. record.Contradicts || MeterTags.IsOdd(record.Applied)
                    ? [TrackMark.OddBars]
                    : Array.Empty<TrackMark>(),
            ];

    /// <summary>Carries the stems' changes to the song's record; true when the record changed.</summary>
    public bool Settle(string song, IReadOnlyList<StemMeter> stems)
    {
        lock (_gate)
        {
            var records = Load();
            var held = records.GetValueOrDefault(song);
            var outcome = MeterTags.Settle(
                held?.Applied ?? [],
                held?.Retired ?? [],
                held?.RemovalsSeen ?? [],
                stems
            );
            var settled = new MeterRecord
            {
                Song = held?.Song ?? song,
                Applied = outcome.Applied,
                Retired = outcome.Retired,
                RemovalsSeen = outcome.RemovalsSeen,
                Contradicts = outcome.Contradicts,
            };

            if (held is null ? settled is { Applied: [], Contradicts: false } : settled == held)
                return false;

            records[settled.Song] = settled;
            Save(records);

            return true;
        }
    }

    private Dictionary<string, MeterRecord> Load() =>
        new(
            MeterJsonContext.Default.MeterFile.ReadFileOrDefault(_path)?.Songs
                ?? new Dictionary<string, MeterRecord>(),
            StringComparer.OrdinalIgnoreCase
        );

    private void Save(Dictionary<string, MeterRecord> records) =>
        MeterJsonContext.Default.MeterFile.WriteFile(_path, new MeterFile { Songs = records });
}

/// <summary>One song's Meter tags as crateside last applied them.</summary>
public sealed record MeterRecord
{
    public required string Song { get; init; }

    public IReadOnlyList<string> Applied { get; init; } = [];

    public IReadOnlyList<string> Retired { get; init; } = [];

    public IReadOnlyList<string> RemovalsSeen { get; init; } = [];

    /// <summary>The stems disagree, so nothing is copied and the track is flagged.</summary>
    public bool Contradicts { get; init; }

    public bool Equals(MeterRecord? other) =>
        other is not null
        && Song == other.Song
        && Contradicts == other.Contradicts
        && Applied.SequenceEqual(other.Applied)
        && Retired.SequenceEqual(other.Retired)
        && RemovalsSeen.SequenceEqual(other.RemovalsSeen);

    public override int GetHashCode() => HashCode.Combine(Song, Contradicts, Applied.Count);
}

public sealed record MeterFile
{
    public Dictionary<string, MeterRecord> Songs { get; init; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(MeterFile))]
internal sealed partial class MeterJsonContext : JsonSerializerContext { }
