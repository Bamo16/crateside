using System.Text.Json.Serialization;
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
    public bool TryPickNext(WorkListsView view, out WantedDownload next)
    {
        next = Kept(view) ?? Added(view) ?? Audio(view)!;

        return next is not null;
    }

    /// <summary>
    /// A track with no audio held for it yet, from both work lists: a re-cut track has a verdict
    /// and an empty working set.
    /// </summary>
    private WantedDownload? Audio(WorkListsView view) =>
        AudioCandidates(view)
            .Where(item => WhyNotAudio(item) is null)
            .Select(item => new WantedDownload
            {
                TrackKey = item.TrackKey,
                Url = item.StatedSourceUrl is { Length: > 0 } stated ? stated : item.WatchUrl,
                Into = workingSet.Prepare(item.TrackKey),
                Track = item,
            })
            .FirstOrDefault();

    /// <summary>Every track that could need audio, with why the audio pass will or will not fetch it.</summary>
    public IReadOnlyList<Decision> Decisions(WorkListsView view) =>
        [
            .. AudioCandidates(view)
                .Select(item => new Decision(
                    item.TrackKey,
                    item.Title,
                    Selection.From(WhyNotAudio(item))
                )),
        ];

    private static IEnumerable<WorkItem> AudioCandidates(WorkListsView view) =>
        view
            .Outstanding.Concat(view.AwaitingSeparation)
            .DistinctBy(item => item.TrackKey, StringComparer.Ordinal);

    private SkipCause? WhyNotAudio(WorkItem item) =>
        item.IsUnavailable ? SkipCause.Unavailable
        : !item.CanBeFetched ? SkipCause.NotFetchable
        : workingSet.TryGetSource(item.TrackKey, out _) ? SkipCause.AlreadyHasAudio
        : IsSweptUp(item) ? SkipCause.SweptUp
        : WhyNotEligible(item.TrackKey);

    /// <summary>
    /// A track whose plan keeps its separation source and has no copy made: copied from the audio
    /// held, or the audio fetched first when it is not (ADR 0019).
    /// </summary>
    private WantedDownload? Kept(WorkListsView view) =>
        view
            .Everything.Where(item => item is { Plan.KeepSource: true, Inventory.HasSource: false })
            .Select(KeptSource)
            .OfType<WantedDownload>()
            .FirstOrDefault();

    private WantedDownload? KeptSource(WorkItem item)
    {
        if (workingSet.TryGetSource(item.TrackKey, out var held))
            return AwaitingHandoff(Path.GetFileName(held))
                ? null
                : new WantedDownload
                {
                    TrackKey = item.TrackKey,
                    Url = item.WatchUrl,
                    Track = item,
                    Copy = true,
                };

        // A track finished with owes nothing unasked (ADR 0027): only a request not yet carried
        // through, or a handoff still owed, fetches its source again.
        if (
            IsSweptUp(item)
                && CutRequests.IsSettled(item, jobs)
                && !StageEligibility.Owes(jobs, item.TrackKey, JobStage.Handoff)
            || !item.CanBeFetched
            || !IsEligible(item.TrackKey)
        )
            return null;

        // Into the working set, so the next pass copies it the same way as audio already held.
        return new WantedDownload
        {
            TrackKey = item.TrackKey,
            Url = item.StatedSourceUrl is { Length: > 0 } stated ? stated : item.WatchUrl,
            Into = workingSet.Prepare(item.TrackKey),
            Track = item,
        };
    }

    /// <summary>
    /// A source the user attached that is not on disk, from every work list: one can be added
    /// even to a finished track.
    /// </summary>
    private WantedDownload? Added(WorkListsView view) =>
        view
            .Everything.SelectMany(item =>
                item.Plan.AddedSources.Where(added =>
                        !item.Inventory.Has(added)
                        && !AwaitingHandoff($"*({added.Label}).*")
                        && IsEligible(added.TrackKey)
                    )
                    .Select(added => new WantedDownload
                    {
                        TrackKey = added.TrackKey,
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
    private bool IsEligible(string trackKey) => WhyNotEligible(trackKey) is null;

    private SkipCause? WhyNotEligible(string trackKey) =>
        StageEligibility.WhyNot(jobs, trackKey, JobStage.Download, maxStrikes, retrySucceeded: true)
        ?? (IsWaitingOut(trackKey) ? SkipCause.WaitingOutBackoff : null);

    private bool IsWaitingOut(string trackKey) =>
        jobs.TryGet(trackKey, JobStage.Download, out var job)
        && job is { Status: JobStatus.Failed, Strikes: var strikes }
        && _time.GetUtcNow() < job.UpdatedAt + Backoff(strikes);

    /// <summary>
    /// The audio is absent because the janitor took it after separation succeeded, not because it
    /// was never fetched — unless a re-cut or swap has asked for another separation since (#50).
    /// </summary>
    private bool IsSweptUp(WorkItem item) =>
        jobs.TryGet(item.TrackKey, JobStage.Separation, out var job)
        && job is { Status: JobStatus.Succeeded }
        && !CutRequests.OwesCut(item, jobs);
}

/// <summary>One source crateside owes.</summary>
public sealed record WantedDownload
{
    /// <summary>The video this fetches, which is what the job is recorded against.</summary>
    [JsonPropertyName("videoId")]
    public required string TrackKey { get; init; }

    public required string Url { get; init; }

    /// <summary>The track this belongs to, for what the drainer reports.</summary>
    public required WorkItem Track { get; init; }

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
