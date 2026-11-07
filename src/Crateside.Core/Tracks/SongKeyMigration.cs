using Crateside.Core.Ledger;

namespace Crateside.Core.Tracks;

/// <summary>
/// Startup step: records kept under a video ID whose track has since been judged move to its song
/// (ADR 0071).
/// </summary>
public sealed class SongKeyMigration(TrackAnnotationReader annotations, LedgerStore ledger)
{
    /// <summary>Returns how many records moved; none on a second run.</summary>
    public int Run() => annotations.FollowSongs(ledger.Read());
}
