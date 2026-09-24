using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Takes a track out of crateside, records before files, and never reaches past
/// <c>Sample Staging</c> (ADR 0027, #33).
/// </summary>
public sealed class TrackRemoval(
    QueueSettingsStore settings,
    IntakeStore intake,
    WorkingSet workingSet,
    ReadStagedFiles staged,
    DeletionLog? deletions = null
)
{
    private readonly DeletionLog _deletions = deletions ?? DeletionLog.None;

    /// <summary>The staged files removing this track would recycle, never an adopted sample.</summary>
    public IReadOnlyList<StagingStem> Recycling(IReadOnlyList<string> uploads) =>
        [
            .. uploads
                .Distinct(StringComparer.Ordinal)
                .SelectMany(upload => staged(upload))
                .Where(stem => !stem.IsAdopted),
        ];

    public TrackRemovalOutcome Apply(string videoId, IReadOnlyList<string> uploads)
    {
        var recycling = Recycling(uploads);

        // Both, because either one alone puts the track back on the next scan (ADR 0021).
        settings.Forget(videoId);
        intake.Forget(videoId);

        foreach (var upload in uploads.Distinct(StringComparer.Ordinal))
            workingSet.Discard(upload);

        var recycled = 0;

        foreach (var stem in recycling)
        {
            try
            {
                _deletions.Recycle(stem.Path, "removed with the track");
                RecycleBin.Remove(StemFileName.AnalysisFor(stem.Path));

                recycled++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new TrackRemovalOutcome
                {
                    Recycled = recycled,
                    Problem =
                        $"The track is removed, but {stem.FileName} could not be recycled — it "
                        + "is probably open in Ableton. Delete it by hand.",
                };
            }
        }

        return new TrackRemovalOutcome { Recycled = recycled };
    }
}

public sealed record TrackRemovalOutcome
{
    public int Recycled { get; init; }

    public string? Problem { get; init; }
}
