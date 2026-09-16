using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>Decides which source to acquire next.</summary>
/// <remarks>
/// Three kinds, and audio comes first: nothing can be judged or cut until it is here, where a
/// kept source and an added one are each only one more sample (ADR 0019).
/// </remarks>
public sealed class DownloadSelector(
    JobStore jobs,
    WorkingSet workingSet,
    CratesidePaths paths,
    int maxStrikes = DownloadSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 5;

    /// <remarks>
    /// Audio comes last of the three, having once come first. Both of the others are bounded by
    /// something the user did — a plan that keeps its source, a source they attached — while the
    /// audio list refills from the playlist, so ordering audio first starves the other two for as
    /// long as anything is arriving. A source added to a finished track waited that way and was
    /// never fetched.
    /// </remarks>
    public bool TryPickNext(QueueView view, out WantedDownload next)
    {
        next = Kept(view) ?? Added(view) ?? Audio(view)!;

        return next is not null;
    }

    /// <summary>
    /// A track with no audio held for it yet.
    /// </summary>
    /// <remarks>
    /// Both work lists, not just the outstanding one: a track whose stems were cut and then
    /// re-cut has a verdict and an empty working set, and separation used to cover that by
    /// handing the URL to the separator — acquisition inside the stage that separates.
    /// </remarks>
    private WantedDownload? Audio(QueueView view) =>
        view
            .Outstanding.Concat(view.AwaitingSeparation)
            .DistinctBy(item => item.VideoId, StringComparer.Ordinal)
            .Where(item =>
                !item.IsUnavailable
                && !workingSet.TryGetSource(item.VideoId, out _)
                && !IsSweptUp(item.VideoId)
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
    /// A track whose plan keeps its separation source, with the audio held and no copy made.
    /// </summary>
    /// <remarks>
    /// Copied rather than separated, so it has no business in the stage that spends the GPU —
    /// it used to live there because <c>--keep-source</c> was a flag on <c>separate</c>, which
    /// meant pausing separation stopped a file copy (ADR 0019).
    /// </remarks>
    private WantedDownload? Kept(QueueView view) =>
        view
            .Everything.Where(item =>
                item.Plan.KeepSource
                && !item.Inventory.HasSource
                && workingSet.TryGetSource(item.VideoId, out var held)
                && !AwaitingHandoff(Path.GetFileName(held))
            )
            .Select(item => new WantedDownload
            {
                VideoId = item.VideoId,
                Url = item.WatchUrl,
                Track = item,
                Copy = true,
            })
            .FirstOrDefault();

    /// <summary>
    /// A source the user attached that is not on disk.
    /// </summary>
    /// <remarks>
    /// Read from every work list rather than just the outstanding one: a source can be added to
    /// a track at any point, including one that was finished with.
    /// </remarks>
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
                        Track = item,
                    })
            )
            .FirstOrDefault();

    /// <summary>
    /// The file has been fetched and is waiting for the handoff to move it into staging.
    /// </summary>
    /// <remarks>
    /// The inventory reads <c>Sample Staging</c> and <c>Mashup Samples</c>, so a file in the
    /// stems output is invisible to it — and the gap between a download finishing and a handoff
    /// running is long enough to fetch the same upload 280 times, which is what it did.
    /// </remarks>
    private bool AwaitingHandoff(string pattern) =>
        new[] { paths.StemsKeyIdentified, paths.StemsUnresolvedKey }
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, pattern))
            .Any();

    /// <remarks>
    /// retrySucceeded is true: the checks above already confirmed the file is gone, and both
    /// the working set and staging may be emptied by hand at any time.
    /// </remarks>
    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Download, maxStrikes, retrySucceeded: true);

    /// <summary>
    /// The audio is absent because the janitor took it, not because it was never fetched.
    /// </summary>
    /// <remarks>
    /// The eligibility check above retries after a success on purpose: the working set can be
    /// emptied by hand and the file really is gone. But the janitor empties it too, once
    /// separation has succeeded and staging is clear — so a finished track with no working set
    /// looks exactly like an unfetched one, and re-downloading it makes the janitor sweep it
    /// again. That ran every ten minutes for a day, 125 times on one track.
    ///
    /// A re-cut is not caught by this: putting a track back in line reopens its separation job,
    /// so the latest is no longer a success.
    /// </remarks>
    private bool IsSweptUp(string videoId) =>
        jobs.TryGet(videoId, JobStage.Separation, out var job)
        && job is { Status: JobStatus.Succeeded };
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

    /// <summary>The audio is already held, so this is a copy out of the working set.</summary>
    public bool Copy { get; init; }

    public bool IsAdded => Label is not null;
}

/// <summary>Fetches one source at a time, for as long as any is owed.</summary>
public sealed class DownloadDrainer(
    QueueService queue,
    DownloadRunner runner,
    DownloadSelector selector,
    JobStore jobs,
    WorkingSet workingSet,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Download;

    public async Task<StageTick> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        QueueView view;

        try
        {
            view = await queue.ReadAsync(cancellationToken);
        }
        catch (QueueUnavailableException e)
        {
            return StageTick.Unavailable(e.Message);
        }

        if (!selector.TryPickNext(view, out var wanted))
            return StageTick.Idle;

        var ledger = KeyLedger.Read(_paths.Ledger);
        var order = WorkOrder.Compose(wanted.Track, ledger);

        jobs.Record(wanted.VideoId, JobStage.Download, JobStatus.Running);

        var destination = _paths.DestinationFor(order.Destination);

        var error = wanted.Copy
            ? Copied(wanted.VideoId, destination)
            : await FetchedAsync(wanted, order, destination, cancellationToken);

        jobs.Record(
            wanted.VideoId,
            JobStage.Download,
            error is null ? JobStatus.Succeeded : JobStatus.Failed,
            error: error
        );

        return StageTick.Ran(order, error is null, error);
    }

    private async Task<string?> FetchedAsync(
        WantedDownload wanted,
        WorkOrder order,
        string destination,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(
            wanted.Url,
            wanted.Into ?? destination,
            order.Recipe.Format,
            cancellationToken
        );

        return result.IsSuccess ? Placed(wanted, result) : result.Error;
    }

    /// <summary>
    /// Copies the separation source into the destination, for a plan that keeps it as a sample.
    /// </summary>
    /// <remarks>
    /// The same bytes separation is cut from, so there is nothing to fetch and nothing to check
    /// about its quality — it is the file the track was judged on.
    /// </remarks>
    private string? Copied(string videoId, string destination)
    {
        if (!workingSet.TryGetSource(videoId, out var held))
            return "the audio to copy is no longer on disk";

        try
        {
            Directory.CreateDirectory(destination);
            File.Copy(held, Path.Combine(destination, Path.GetFileName(held)), overwrite: true);

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the source could not be copied: {e.Message}";
        }
    }

    /// <summary>
    /// Puts the fetched file where the rest of the pipeline expects it, and keeps what the
    /// download said about its own audio.
    /// </summary>
    /// <remarks>
    /// An added source's quality is recorded against its own video ID, so a poor one is a
    /// finding about that file and never parks the track (ADR 0019).
    /// </remarks>
    private string? Placed(WantedDownload wanted, DownloadResult result)
    {
        workingSet.SaveAcquisition(wanted.VideoId, result.Acquisition!);

        return wanted.Label is { } label ? Suffixed(result.Path!, label) : null;
    }

    /// <summary>
    /// Renames a fetched source to carry its label, which the handoff then preserves while it
    /// swaps the track-name half.
    /// </summary>
    /// <remarks>
    /// Without a suffix the file reads as though its whole name were the track's, so a rename
    /// would append rather than replace and two sources would collide.
    /// </remarks>
    private static string? Suffixed(string path, string label)
    {
        var renamed = Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(path)} ({label}){Path.GetExtension(path)}"
        );

        try
        {
            File.Move(path, renamed, overwrite: true);

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"downloaded, but could not be named ({label}): {e.Message}";
        }
    }
}
