using System.Text.Json.Serialization;

namespace Crateside.Core.Tuning;

/// <summary>What was decided about each track's tuning, kept across restarts in <c>tuning.json</c> (ADR 0101).</summary>
public sealed class TuningStore
{
    private readonly Lock _lock = new();
    private readonly string? _path;
    private readonly Dictionary<string, TuningEntry> _entries;

    public TuningStore(CratesidePaths paths)
        : this(paths.TuningFile) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal TuningStore(string? path = null)
    {
        _path = path;
        _entries = new(
            path is not null
            && TuningJsonContext.Default.DictionaryStringTuningEntry.ReadFileOrDefault(path)
                is { } entries
                ? entries
                : [],
            StringComparer.Ordinal
        );
    }

    public TuningEntry? Of(string trackKey)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(trackKey);
        }
    }

    public void Save(string trackKey, TuningEntry entry)
    {
        lock (_lock)
        {
            _entries[trackKey] = entry;

            if (_path is not null)
                TuningJsonContext.Default.DictionaryStringTuningEntry.WriteFile(_path, _entries);
        }
    }

    /// <summary>The tracks waiting for the user's word, by song.</summary>
    public IReadOnlyList<(string TrackKey, TuningEntry Entry)> Review()
    {
        lock (_lock)
        {
            return
            [
                .. _entries
                    .Where(pair => pair.Value.State is TuningState.Review)
                    .Select(pair => (pair.Key, pair.Value))
                    .OrderBy(pair => pair.Value.Song, StringComparer.CurrentCultureIgnoreCase),
            ];
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<TuningState>))]
public enum TuningState
{
    /// <summary>The correction is written, or waits for each sample's saved clip.</summary>
    Written,

    /// <summary>Not written: unsteady, or near a quarter tone; waits for the user.</summary>
    Review,

    /// <summary>Under the threshold; nothing to write.</summary>
    InTune,

    /// <summary>Put back by the user; the pass does not write it again.</summary>
    LeftAlone,
}

/// <summary>One track's decision.</summary>
public sealed record TuningEntry
{
    public required TuningState State { get; init; }

    public string? Song { get; init; }

    /// <summary>Cents from A = 440.</summary>
    public double? Median { get; init; }

    public IReadOnlyList<double> Readings { get; init; } = [];

    /// <summary>Why a track is on the review list.</summary>
    public string? Why { get; init; }

    public double? Correction { get; init; }

    /// <summary>The detune crateside wrote into each sample's clip, by audio path; the clip held 0 before.</summary>
    public IReadOnlyDictionary<string, double> Written { get; init; } =
        new Dictionary<string, double>();

    public DateTimeOffset At { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
)]
[JsonSerializable(typeof(Dictionary<string, TuningEntry>))]
internal sealed partial class TuningJsonContext : JsonSerializerContext { }
