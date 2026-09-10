using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// A song's staged files cut from an upload it is no longer cut from.
/// </summary>
/// <remarks>
/// Only ever in <c>Sample Staging</c>, which crateside manages. A superseded sample that has
/// reached <c>Mashup Samples</c> is the user's to remove, and Ableton asks before overwriting one
/// (ADR 0014).
/// </remarks>
public static class SupersededStems
{
    /// <summary>
    /// Files in <paramref name="stagingRoot"/> cut from a different upload of the same song as
    /// <paramref name="videoId"/>. Empty for a track the ledger has never judged, which cannot
    /// have been swapped.
    /// </summary>
    public static IReadOnlyList<string> In(string stagingRoot, string videoId, KeyLedger ledger)
    {
        if (ledger.SongFor(videoId) is not { } song || !Directory.Exists(stagingRoot))
            return [];

        return
        [
            .. Directory
                .EnumerateFiles(stagingRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(StagingBoard.IsStemFile)
                .Select(StemTagReader.Read)
                .OfType<StemTags>()
                .Where(tags => IsAnEarlierUpload(tags, videoId, song, ledger))
                .Select(tags => tags.Path),
        ];
    }

    private static bool IsAnEarlierUpload(
        StemTags tags,
        string videoId,
        string song,
        KeyLedger ledger
    ) =>
        tags.SourceVideoId is { Length: > 0 } id
        && !id.Equals(videoId, StringComparison.Ordinal)
        && song.Equals(ledger.SongFor(id), StringComparison.Ordinal);
}
