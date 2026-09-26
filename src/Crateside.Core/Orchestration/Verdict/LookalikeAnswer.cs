using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Settles whether a track is the judged song its name looks like (#41).</summary>
public sealed class LookalikeAnswer(
    QueueSettingsStore settings,
    CratesidePaths paths,
    string uv = "uv"
)
{
    /// <summary>
    /// Has keytag file this upload under <paramref name="song"/>, so its verdict and files carry
    /// across and nothing is judged or cut again. Null on success, else why not.
    /// </summary>
    public async Task<string?> SameSongAsync(
        string videoId,
        LedgerRun song,
        CancellationToken cancellationToken = default
    )
    {
        if (song.SourceVideoId is not { } judged)
            return "The ledger does not say which upload that song was judged from.";

        var outcome = await ExternalProcess.RunAsync(
            uv,
            [
                "run",
                "--project",
                paths.KeytagRepo,
                "keytag-restate",
                "--from",
                judged,
                "--to",
                $"https://music.youtube.com/watch?v={videoId}",
            ],
            workingDirectory: null,
            cancellationToken
        );

        return outcome.Succeeded
            ? null
            : ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                ?? $"keytag-restate exited with {outcome.ExitCode}";
    }

    /// <summary>Records that the track is a different song, which lets judging have it.</summary>
    public void DifferentSong(string videoId, LedgerRun song) =>
        settings.SetDifferentFrom(videoId, song.Song);
}
