using Crateside.Core.Ledger;

namespace Crateside.Core.Tracks;

/// <summary>
/// Startup step: records kept under a video ID whose track has since been judged move to its song,
/// each changed file copied to <c>.bak-</c> beside it first (ADR 0071).
/// </summary>
public sealed class SongKeyMigration(
    TrackAnnotationReader annotations,
    LedgerStore ledger,
    TimeProvider time
)
{
    /// <summary>Returns how many records moved; none on a second run.</summary>
    public int Run() => annotations.FollowSongs(ledger.Read(), BackUp);

    private void BackUp(string path)
    {
        if (File.Exists(path))
            File.Copy(path, $"{path}.bak-{time.GetLocalNow():yyyyMMddHHmmss}", overwrite: true);
    }
}
