using Microsoft.Extensions.Options;

namespace Crateside.Core.Ledger;

/// <summary>
/// Fetches the evidence behind a verdict, by song identity or by the video ID a stem carries.
/// </summary>
public sealed class EvidenceService(IOptions<CratesidePaths> paths)
{
    private readonly CratesidePaths _paths = paths.Value;

    public RunEvidence? ForSong(string song) => RunEvidence.ReadLatestFor(_paths.Ledger, song);

    /// <summary>
    /// Resolves a video ID to its song identity first, then takes that identity's latest run —
    /// what makes a supersede win.
    /// </summary>
    public RunEvidence? ForVideoId(string videoId) =>
        KeyLedger.Read(_paths.Ledger).TryGetLatestByVideoId(videoId, out var run)
            ? ForSong(run.Song)
            : null;
}
