using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Download;

/// <summary>
/// Decides which source to acquire next: audio to cut from, a kept source, or an added one (ADR 0019).
/// </summary>
public sealed class DownloadSelector(
    JobStore jobs,
    WorkingSet workingSet,
    CratesidePaths paths,
    int maxStrikes = DownloadSelector.DefaultMaxStrikes,
    TimeProvider? time = null
)
{
    public const int DefaultMaxStrikes = 5;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Picks audio last, since its list refills from the playlist and would starve the others.</summary>
    public bool TryPickNext(QueueView view, out WantedDownload next)
    {
        next = Kept(view) ?? Added(view) ?? Audio(view)!;

        return next is not null;
    }

    /// <summary>
    /// A track with no audio held for it yet, from both work lists: a re-cut track has a verdict
    /// and an empty working set.
    /// </summary>
    private WantedDownload? Audio(QueueView view) =>
        view
            .Outstanding.Concat(view.AwaitingSeparation)
            .DistinctBy(item => item.VideoId, StringComparer.Ordinal)
            .Where(item =>
                !item.IsUnavailable
                && !workingSet.TryGetSource(item.VideoId, out _)
                && !IsSweptUp(item)
                && IsEligible(item.VideoId)
            )
            .Select(item => new WantedDownload
            {
                VideoId = item.VideoId,
                Url = item.StatedSourceUrl is { Length: > 0 } stated ? stated : item.WatchUrl,
                Into = workingSet.Prepare(item.VideoId),
                Track = item,
            })
            .FirstOrDefault();

    /// <summary>
    /// A track whose plan keeps its separation source and has no copy made: copied from the audio
    /// held, or the audio fetched first when it is not (ADR 0019).
    /// </summary>
    private WantedDownload? Kept(QueueView view) =>
        view
            .Everything.Where(item => item is { Plan.KeepSource: true, Inventory.HasSource: false })
            .Select(KeptSource)
            .OfType<WantedDownload>()
            .FirstOrDefault();

    private WantedDownload? KeptSource(QueueItem item)
    {
        if (workingSet.TryGetSource(item.VideoId, out var held))
            return AwaitingHandoff(Path.GetFileName(held))
                ? null
                : new WantedDownload
                {
                    VideoId = item.VideoId,
                    Url = item.WatchUrl,
                    Track = item,
                    Copy = true,
                };

        // A track finished with owes nothing unasked (ADR 0027): only a request not yet carried
        // through, or a handoff still owed, fetches its source again.
        if (
            IsSweptUp(item)
                && CutRequests.IsSettled(item, jobs)
                && !StageEligibility.Owes(jobs, item.VideoId, JobStage.Handoff)
            || !IsEligible(item.VideoId)
        )
            return null;

        // Into the working set, so the next pass copies it the same way as audio already held.
        return new WantedDownload
        {
            VideoId = item.VideoId,
            Url = item.StatedSourceUrl is { Length: > 0 } stated ? stated : item.WatchUrl,
            Into = workingSet.Prepare(item.VideoId),
            Track = item,
        };
    }

    /// <summary>
    /// A source the user attached that is not on disk, from every work list: one can be added
    /// even to a finished track.
    /// </summary>
    private WantedDownload? Added(QueueView view) =>
        view
            .Everything.SelectMany(item =>
                item.Plan.AddedSources.Where(added =>
                        !item.Inventory.Has(added)
                        && !AwaitingHandoff($"*({added.Label}).*")
                        && IsEligible(added.VideoId)
                    )
                    .Select(added => new WantedDownload
                    {
                        VideoId = added.VideoId,
                        Url = added.WatchUrl,
                        Label = added.Label,
                        Type = added.Type,
                        Track = item,
                    })
            )
            .FirstOrDefault();

    /// <summary>
    /// The file is fetched and waiting for the handoff, where the inventory cannot see it yet — so
    /// it must not be fetched again.
    /// </summary>
    private bool AwaitingHandoff(string pattern) =>
        new[] { paths.StemsKeyIdentified, paths.StemsUnresolvedKey }
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, pattern))
            .Any();

    /// <summary>How long a failed download waits before it is tried again, after this many strikes.</summary>
    public static TimeSpan Backoff(int strikes) =>
        strikes switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromHours(2),
        };

    /// <summary>Retries even a succeeded download, since every caller has already found its file gone.</summary>
    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Download, maxStrikes, retrySucceeded: true)
        && !IsWaitingOut(videoId);

    private bool IsWaitingOut(string videoId) =>
        jobs.TryGet(videoId, JobStage.Download, out var job)
        && job is { Status: JobStatus.Failed, Strikes: var strikes }
        && _time.GetUtcNow() < job.UpdatedAt + Backoff(strikes);

    /// <summary>
    /// The audio is absent because the janitor took it after separation succeeded, not because it
    /// was never fetched — unless a re-cut or swap has asked for another separation since (#50).
    /// </summary>
    private bool IsSweptUp(QueueItem item) =>
        jobs.TryGet(item.VideoId, JobStage.Separation, out var job)
        && job is { Status: JobStatus.Succeeded }
        && !CutRequests.OwesCut(item, jobs);
}

/// <summary>One source crateside owes.</summary>
public sealed record WantedDownload
{
    /// <summary>The video this fetches, which is what the job is recorded against.</summary>
    public required string VideoId { get; init; }

    public required string Url { get; init; }

    /// <summary>The track this belongs to, for what the drainer reports.</summary>
    public required QueueItem Track { get; init; }

    /// <summary>Where the file lands. Null for an added source, which goes by destination.</summary>
    public string? Into { get; init; }

    /// <summary>What the user called it, which becomes its filename suffix. Null for a detection source.</summary>
    public string? Label { get; init; }

    /// <summary>What the user said an added source is. Null for any other download.</summary>
    public SampleType? Type { get; init; }

    /// <summary>The audio is already held, so this is a copy out of the working set.</summary>
    public bool Copy { get; init; }

    public bool IsAdded => Label is not null;
}
