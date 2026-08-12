using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crateside.Core.Ledger;

/// <summary>
/// keytag's <c>results/runs.jsonl</c>, read for lookup. Read-only: keytag owns this file.
/// </summary>
/// <remarks>
/// The lookup that matters is <strong>video ID → song identity → latest run</strong>.
/// Resolving to the identity first is what makes a supersede win: a re-run is filed under the
/// same identity, so going straight from a video ID to "the run that mentioned it" would
/// return a stale verdict whenever the newer run recorded a different URL — or none.
/// </remarks>
public sealed class KeyLedger
{
    private readonly Dictionary<string, LedgerRun> _latestBySong;
    private readonly Dictionary<string, string> _songByVideoId;

    private KeyLedger(IReadOnlyList<LedgerRun> runs)
    {
        Runs = runs;

        // Append-only, so later lines supersede earlier ones for the same identity. Ordering
        // by timestamp rather than file position keeps this correct even if a line is ever
        // appended out of order.
        _latestBySong = runs.GroupBy(r => r.Song, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Timestamp).Last(),
                StringComparer.OrdinalIgnoreCase
            );

        _songByVideoId = runs.Where(r => r.SourceVideoId is not null)
            .GroupBy(r => r.SourceVideoId!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Timestamp).Last().Song,
                StringComparer.Ordinal
            );
    }

    /// <summary>Every run parsed from the file, in file order.</summary>
    public IReadOnlyList<LedgerRun> Runs { get; }

    /// <summary>The number of distinct songs the ledger knows about.</summary>
    public int SongCount => _latestBySong.Count;

    /// <summary>
    /// Reads the ledger. A malformed line is skipped rather than failing the whole read —
    /// the file is append-only and may be caught mid-write.
    /// </summary>
    public static KeyLedger Read(string path)
    {
        if (!File.Exists(path))
            return new KeyLedger([]);

        var runs = new List<LedgerRun>();

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.AsSpan().IsWhiteSpace())
                continue;

            LedgerRunDto? dto;

            try
            {
                dto = JsonSerializer.Deserialize<LedgerRunDto>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (dto is not null && LedgerRun.FromDto(dto) is { } run)
                runs.Add(run);
        }

        return new KeyLedger(runs);
    }

    /// <summary>The current verdict for a song identity — the latest run recorded for it.</summary>
    public bool TryGetLatestBySong(string song, [NotNullWhen(true)] out LedgerRun? run) =>
        _latestBySong.TryGetValue(song, out run);

    /// <summary>
    /// The current verdict for a source video ID: resolve the ID to a song identity, then
    /// take that identity's latest run — which may well be a run that carries a different
    /// URL, or none at all.
    /// </summary>
    public bool TryGetLatestByVideoId(string videoId, [NotNullWhen(true)] out LedgerRun? run)
    {
        run = null;

        return _songByVideoId.TryGetValue(videoId, out var song)
            && TryGetLatestBySong(song, out run);
    }

    /// <summary>
    /// Whether any run has been recorded for this video ID, regardless of verdict. This is
    /// the ledger half of the "fully processed" test — a WITHHELD verdict still means the
    /// track was worked, so it still counts.
    /// </summary>
    public bool HasRunFor(string videoId) => _songByVideoId.ContainsKey(videoId);
}
