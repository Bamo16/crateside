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
    private readonly Lock _gate = new();

    private List<RejudgeEntry>? _entries;

    public RejudgeQueue(IOptions<CratesidePaths> paths)
        : this(paths.Value.RejudgeFile) { }

    internal RejudgeQueue(string path) => _path = path;

    /// <summary>Every song ever queued. Never throws: an unreadable file reads as empty.</summary>
    public IReadOnlyList<RejudgeEntry> Read()
    {
        lock (_gate)
            return [.. Load()];
    }

    /// <summary>Queues a song to be judged again unless a run from <paramref name="before"/> on is already recorded; a song already queued keeps the later cutoff.</summary>
    public void Add(string song, DateTimeOffset before, string reason, string? requestedBy = null)
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
                    RequestedBy = requestedBy,
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

    private List<RejudgeEntry> Load() =>
        _entries ??= RejudgeJsonContext.Default.RejudgeFile.ReadFileOrDefault(_path)?.Songs
            is { } held
            ? [.. held]
            : [];

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
}

public sealed record RejudgeEntry
{
    /// <summary>The ledger's song identity.</summary>
    public required string Song { get; init; }

    /// <summary>A run recorded at or after this settles the entry.</summary>
    public required DateTimeOffset Before { get; init; }

    public required string Reason { get; init; }

    /// <summary>Who queued it through the API; null for the maintainer or a correction.</summary>
    public string? RequestedBy { get; init; }
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
