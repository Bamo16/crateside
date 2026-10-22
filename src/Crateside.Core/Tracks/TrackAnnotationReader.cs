using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Core.Tracks;

public sealed class TrackAnnotationReader(
    ResolvedNameStore names,
    WorkingSet workingSet,
    DeletionLog deletions,
    GenreStore genres,
    FamiliarityStore familiarity,
    TrackMarkStore marks
)
{
    public TrackAnnotations Read() =>
        new()
        {
            ResolvedNames = names.Read(),
            Acquisitions = workingSet.Acquisitions(),
            Deletions = deletions.All(),
            Genres = genres.Read(),
            Familiarity = familiarity.Read(),
            Marks = marks.Read(),
        };
}
