using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Playlist;

namespace Crateside.Core.Tracks;

public sealed class TrackAnnotationReader(
    ResolvedNameStore names,
    WorkingSet workingSet,
    DeletionLog deletions,
    GenreStore genres,
    FamiliarityStore familiarity,
    TrackMarkStore marks,
    IntakeStore? intake = null,
    MeasuringExclusionStore? exclusions = null
)
{
    /// <summary>Everything recorded about the tracks, once what was kept under a judged upload has moved to its song.</summary>
    public TrackAnnotations Read(KeyLedger ledger)
    {
        FollowSongs(ledger);

        return new()
        {
            ResolvedNames = names.Read(),
            Acquisitions = workingSet.Acquisitions(),
            Deletions = deletions.All(),
            Genres = genres.Read(),
            Familiarity = familiarity.Read(),
            Marks = marks.Read(),
            Releases =
                intake
                    ?.Read()
                    .Where(record => record.Release is not null)
                    .ToDictionary(record => record.TrackKey, record => record.Release!)
                ?? [],
        };
    }

    /// <summary>
    /// Moves every genre, familiarity, mark and measuring-exclusion record kept under a video ID onto the song the
    /// ledger has since judged it as; returns how many moved (ADR 0071).
    /// </summary>
    public int FollowSongs(KeyLedger ledger) =>
        genres.FollowSongs(ledger.SongFor)
        + familiarity.FollowSongs(ledger.SongFor)
        + marks.FollowSongs(ledger.SongFor)
        + (exclusions?.FollowSongs(ledger.SongFor) ?? 0);
}
