using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// A song's staged files cut from an upload it is no longer cut from — in staging only; the
/// library's are the user's (ADR 0014).
/// </summary>
public static class SupersededStems
{
    /// <summary>
    /// Files in <paramref name="stagingRoot"/> cut from a different upload of the same song as
    /// <paramref name="videoId"/>; none for a track never judged.
    /// </summary>
    /// <param name="kept">
    /// Uploads the user asked this song to keep, which are never superseded. Without it a swap's
    /// rule eats every added source of the song (ADR 0019).
    /// </param>
    public static IReadOnlyList<string> In(
        string stagingRoot,
        string videoId,
        KeyLedger ledger,
        IReadOnlySet<string>? kept = null
    )
    {
        if (ledger.SongFor(videoId) is not { } song || !Directory.Exists(stagingRoot))
            return [];

        if (kept?.Contains(videoId) is true)
            return [];

        return
        [
            .. Directory
                .EnumerateFiles(stagingRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(StagingBoard.IsStemFile)
                .Select(StemTagReader.Read)
                .OfType<StemTags>()
                .Where(tags => IsAnEarlierUpload(tags, videoId, song, ledger, kept))
                .Select(tags => tags.Path),
        ];
    }

    private static bool IsAnEarlierUpload(
        StemTags tags,
        string videoId,
        string song,
        KeyLedger ledger,
        IReadOnlySet<string>? kept
    ) =>
        tags.SourceVideoId is { Length: > 0 } id
        && !id.Equals(videoId, StringComparison.Ordinal)
        && kept?.Contains(id) is not true
        && song.Equals(ledger.SongFor(id), StringComparison.Ordinal);
}
