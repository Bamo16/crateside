using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Staging;

/// <summary>
/// The staging board, read fresh from disk on every call.
/// </summary>
/// <remarks>
/// Deliberately uncached. The board is purely derived, and the underlying files change
/// outside crateside — Ableton flushes its XMP when it feels like it, keytag appends runs,
/// the user moves stems out. A cache would only ever serve a staler answer than the disk
/// already has, and the reads are milliseconds.
/// </remarks>
public sealed class StagingBoardService(IOptions<CratesidePaths> paths, StagingBoard board)
{
    private readonly CratesidePaths _paths = paths.Value;

    public string StagingRoot => _paths.SampleStaging;

    /// <summary>Reads the ledger and the staging folder, and rolls them up into songs.</summary>
    public IReadOnlyList<StagingSong> Read() =>
        board.Build(_paths.SampleStaging, KeyLedger.Read(_paths.Ledger));
}
