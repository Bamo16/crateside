namespace Crateside.Core;

/// <summary>Moves records kept under a video ID onto the song the ledger has since judged it as (ADR 0071).</summary>
internal static class SongKeys
{
    /// <summary>Re-keys every record whose key the ledger resolves to a song, merging into any the song already has; returns how many moved.</summary>
    public static int Follow<T>(
        Dictionary<string, T> records,
        Func<string, string?> songFor,
        Func<string, T, T?, T> merge
    )
        where T : class
    {
        var moved = 0;

        foreach (var key in records.Keys.ToArray())
        {
            if (
                songFor(key) is not { } song
                || song.Equals(key, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            var record = records[key];
            records.Remove(key);
            records[song] = merge(song, record, records.GetValueOrDefault(song));
            moved++;
        }

        return moved;
    }
}
