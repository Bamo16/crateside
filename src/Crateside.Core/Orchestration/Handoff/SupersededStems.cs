using Crateside.Core.Ledger;
using Crateside.Core.Samples;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// A song's staged files cut from an upload it is no longer cut from — in staging only; the
/// library's are the user's (ADR 0014).
/// </summary>
public static class SupersededStems
{
    /// <summary>
    /// Files in <paramref name="stagingRoot"/> cut from a different upload of the same song as
    /// <paramref name="trackKey"/>; none for a track never judged.
    /// </summary>
    /// <param name="kept">
    /// Uploads the user asked this song to keep, which are never superseded. Without it a swap's
    /// rule eats every added source of the song (ADR 0019).
    /// </param>
    public static IReadOnlyList<string> In(
        string stagingRoot,
        string trackKey,
        KeyLedger ledger,
        IReadOnlySet<string>? kept = null
    )
    {
        if (ledger.SongFor(trackKey) is not { } song || !Directory.Exists(stagingRoot))
            return [];

        if (kept?.Contains(trackKey) is true)
            return [];

        return
        [
            .. Directory
                .EnumerateFiles(stagingRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(SampleFiles.IsAudio)
                .Select(StemTagReader.Read)
                .OfType<StemTags>()
                .Where(tags => IsAnEarlierUpload(tags, trackKey, song, ledger, kept))
                .Select(tags => tags.Path),
        ];
    }

    private static bool IsAnEarlierUpload(
        StemTags tags,
        string trackKey,
        string song,
        KeyLedger ledger,
        IReadOnlySet<string>? kept
    ) =>
        tags.SourceTrackKey is { Length: > 0 } id
        && !id.Equals(trackKey, StringComparison.Ordinal)
        && kept?.Contains(id) is not true
        && song.Equals(ledger.SongFor(id), StringComparison.Ordinal);
}
