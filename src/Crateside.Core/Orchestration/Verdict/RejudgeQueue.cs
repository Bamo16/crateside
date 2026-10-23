using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>
/// Songs owed a fresh judgment because what their last one saw has since been corrected, kept in
/// <c>%APPDATA%\crateside\rejudge.json</c>. A song is owed until the ledger holds a run for it from
/// <see cref="RejudgeEntry.Before"/> on, so the ledger, not this file, says when it leaves.
/// </summary>
public sealed class RejudgeQueue
{
    private readonly string _path;
    private readonly IReadOnlyList<RejudgeEntry> _seed;
    private readonly Lock _gate = new();

    private List<RejudgeEntry>? _entries;

    public RejudgeQueue(IOptions<CratesidePaths> paths)
        : this(paths.Value.RejudgeFile, ReadSeed()) { }

    internal RejudgeQueue(string path, IReadOnlyList<RejudgeEntry> seed)
    {
        _path = path;
        _seed = seed;
    }

    /// <summary>Every song ever queued, the shipped seed included. Never throws: an unreadable file reads as only the seed.</summary>
    public IReadOnlyList<RejudgeEntry> Read()
    {
        lock (_gate)
            return [.. Load()];
    }

    /// <summary>Queues a song to be judged again unless a run from <paramref name="before"/> on is already recorded; a song already queued keeps the later cutoff.</summary>
    public void Add(string song, DateTimeOffset before, string reason)
    {
        lock (_gate)
        {
            var entries = Load();

            if (entries.Find(entry => Same(entry.Song, song)) is { } held)
            {
                if (held.Before >= before)
                    return;

                entries.Remove(held);
            }

            entries.Add(
                new RejudgeEntry
                {
                    Song = song,
                    Before = before,
                    Reason = reason,
                }
            );
            Save(entries);
        }
    }

    /// <summary>The latest run of each queued song that has no run from its cutoff on, in queue order.</summary>
    public IReadOnlyList<LedgerRun> Pending(KeyLedger ledger) =>
        [
            .. Read()
                .Select(entry =>
                    ledger.TryGetLatestBySong(entry.Song, out var latest)
                    && !ledger.Runs.Any(run =>
                        Same(run.Song, entry.Song)
                        && run.Restates is null
                        && run.Timestamp >= entry.Before
                    )
                        ? latest
                        : null
                )
                .OfType<LedgerRun>(),
        ];

    // Seeding is idempotent by song: a song already in the file is never added again, and one
    // re-judged since its cutoff is not pending whether or not its entry is still there.
    private List<RejudgeEntry> Load()
    {
        if (_entries is not null)
            return _entries;

        var entries = RejudgeJsonContext.Default.RejudgeFile.ReadFileOrDefault(_path)?.Songs
            is { } held
            ? [.. held]
            : new List<RejudgeEntry>();
        var missing = _seed
            .Where(seed => !entries.Exists(entry => Same(entry.Song, seed.Song)))
            .ToList();

        entries.AddRange(missing);

        if (missing is not [])
            Save(entries);

        return _entries = entries;
    }

    private void Save(List<RejudgeEntry> entries)
    {
        try
        {
            RejudgeJsonContext.Default.RejudgeFile.WriteFile(
                _path,
                new RejudgeFile { Songs = [.. entries] }
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool Same(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<RejudgeEntry> ReadSeed()
    {
        using var stream = typeof(RejudgeQueue).Assembly.GetManifestResourceStream(
            "rejudge-seed.json"
        )!;

        return JsonSerializer.Deserialize(stream, RejudgeJsonContext.Default.RejudgeFile)!.Songs;
    }
}

public sealed record RejudgeEntry
{
    /// <summary>The ledger's song identity.</summary>
    public required string Song { get; init; }

    /// <summary>A run recorded at or after this settles the entry.</summary>
    public required DateTimeOffset Before { get; init; }

    public required string Reason { get; init; }
}

internal sealed record RejudgeFile
{
    public IReadOnlyList<RejudgeEntry> Songs { get; init; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(RejudgeFile))]
internal sealed partial class RejudgeJsonContext : JsonSerializerContext { }
