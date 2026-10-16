using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Ledger;

/// <summary>
/// The ledger as it stood when read from <see cref="LedgerStore"/>, looked up video ID → song →
/// latest run so a supersede wins.
/// </summary>
public sealed class KeyLedger
{
    private readonly Dictionary<string, LedgerRun> _latestBySong;
    private readonly Dictionary<string, string> _songByVideoId;

    public static KeyLedger Empty { get; } = new([]);

    /// <summary>Every run and restatement, oldest first.</summary>
    public IReadOnlyList<LedgerRun> Runs { get; }

    /// <summary>The number of distinct songs the ledger knows about.</summary>
    public int SongCount => _latestBySong.Count;

    /// <summary>The current verdict of every song.</summary>
    public IEnumerable<LedgerRun> Latest => _latestBySong.Values;

    internal KeyLedger(IReadOnlyList<LedgerRun> runs)
    {
        Runs = runs;

        _latestBySong = runs.GroupBy(run => run.Song, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                song => song.Key,
                song => song.OrderBy(run => run.Timestamp).Last(),
                StringComparer.OrdinalIgnoreCase
            );

        _songByVideoId = runs.Where(run => run.SourceVideoId is not null)
            .GroupBy(run => run.SourceVideoId!, StringComparer.Ordinal)
            .ToDictionary(
                upload => upload.Key,
                upload => upload.OrderBy(run => run.Timestamp).Last().Song,
                StringComparer.Ordinal
            );
    }

    /// <summary>The current verdict for a song identity — the latest run recorded for it.</summary>
    public bool TryGetLatestBySong(string song, [NotNullWhen(true)] out LedgerRun? run) =>
        _latestBySong.TryGetValue(song, out run);

    /// <summary>
    /// The song a video ID belongs to, or null where the ledger has never seen it. Every URL a
    /// song has been cut from resolves to the same answer (ADR 0014).
    /// </summary>
    public string? SongFor(string videoId) => _songByVideoId.GetValueOrDefault(videoId);

    public bool TryGetLatestByVideoId(string videoId, [NotNullWhen(true)] out LedgerRun? run)
    {
        run = null;

        return _songByVideoId.TryGetValue(videoId, out var song)
            && TryGetLatestBySong(song, out run);
    }

    /// <summary>
    /// Whether any run has been recorded for this video ID, regardless of verdict — a
    /// WITHHELD run still counts as worked.
    /// </summary>
    public bool HasRunFor(string videoId) => _songByVideoId.ContainsKey(videoId);
}
