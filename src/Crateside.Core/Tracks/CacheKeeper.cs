using Crateside.Core.Ableton;
using Crateside.Core.Orchestration;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Tracks;

/// <summary>
/// Keeps two files current as the app reads: <c>source-index.json</c> after each scan of the
/// queue (#133), and the warp tempo cache after each rebuild of the Tracks rows, which is what
/// reads the tempos.
/// </summary>
public sealed class CacheKeeper : IDisposable
{
    private readonly WorkListService _workLists;
    private readonly TracksService _tracks;
    private readonly SourceIndex _sources;
    private readonly WarpTempoReader _warpTempos;

    public CacheKeeper(
        WorkListService workLists,
        TracksService tracks,
        SourceIndex sources,
        WarpTempoReader warpTempos
    )
    {
        _workLists = workLists;
        _tracks = tracks;
        _sources = sources;
        _warpTempos = warpTempos;

        _workLists.Scanned += OnScanned;
        _tracks.Changed += OnRowsChanged;
    }

    public void Dispose()
    {
        _workLists.Scanned -= OnScanned;
        _tracks.Changed -= OnRowsChanged;
    }

    private void OnScanned(QueueScan scan) => _sources.Refresh(scan.Processed, scan.Ledger);

    private void OnRowsChanged() => _warpTempos.Save();
}
