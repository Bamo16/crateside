using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Settles whether a track is the judged song its name looks like (#41).</summary>
public sealed class LookalikeAnswer(LedgerStore store, TrackInstructionStore instructions)
{
    /// <summary>
    /// Files this upload under <paramref name="song"/>, so its verdict and files carry across and
    /// nothing is judged or cut again. Null on success, else why not.
    /// </summary>
    public string? SameSong(string videoId, LedgerRun song) =>
        song.SourceVideoId is { } judged
            ? store.Restate(judged, $"https://music.youtube.com/watch?v={videoId}").Refusal
            : "The ledger does not say which upload that song was judged from.";

    /// <summary>Records that the track is a different song, which lets judging have it.</summary>
    public void DifferentSong(string videoId, LedgerRun song) =>
        instructions.SetDifferentFrom(videoId, song.Song);
}
