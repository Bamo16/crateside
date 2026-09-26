using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>
/// What a re-cut or swap the user asked for still owes, read from the request on the track's
/// instruction against the outcomes that answer it, never from job history as intent (#50).
/// </summary>
public static class CutRequests
{
    /// <summary>
    /// A separation is owed: nothing has succeeded since the request and none is in hand. A failure
    /// since the request is left to the strikes.
    /// </summary>
    public static bool OwesCut(DateTimeOffset? requestedAt, TrackJob? separation) =>
        requestedAt is { } asked
        && separation switch
        {
            null => true,
            { IsOpen: true } => false,
            { UpdatedAt: var at } when at < asked => true,
            _ => false,
        };

    /// <summary>
    /// The request has been carried all the way through: a handoff has succeeded since it, which a
    /// cut and a kept-source copy both end in.
    /// </summary>
    public static bool IsSettled(DateTimeOffset? requestedAt, TrackJob? handoff) =>
        requestedAt is not { } asked
        || handoff is { Status: JobStatus.Succeeded, UpdatedAt: var at } && at >= asked;

    public static bool OwesCut(QueueItem item, JobStore jobs) =>
        OwesCut(item.CutRequestedAt, Latest(jobs, item.VideoId, JobStage.Separation));

    public static bool IsSettled(QueueItem item, JobStore jobs) =>
        IsSettled(item.CutRequestedAt, Latest(jobs, item.VideoId, JobStage.Handoff));

    /// <summary>Video IDs whose request still owes a separation.</summary>
    public static IReadOnlySet<string> Cutting(QueueSettings settings, JobStore jobs) =>
        Where(
            settings,
            track => OwesCut(track.CutRequestedAt, Latest(jobs, track.VideoId, JobStage.Separation))
        );

    /// <summary>Video IDs whose request is not yet carried through, which keeps their rows.</summary>
    public static IReadOnlySet<string> Unsettled(QueueSettings settings, JobStore jobs) =>
        Where(
            settings,
            track => !IsSettled(track.CutRequestedAt, Latest(jobs, track.VideoId, JobStage.Handoff))
        );

    private static IReadOnlySet<string> Where(
        QueueSettings settings,
        Func<TrackInstruction, bool> owing
    ) =>
        settings
            .Tracks.Where(track => track.CutRequestedAt is not null && owing(track))
            .Select(track => track.VideoId)
            .ToHashSet(StringComparer.Ordinal);

    private static TrackJob? Latest(JobStore jobs, string videoId, JobStage stage) =>
        jobs.TryGet(videoId, stage, out var job) ? job : null;
}
