using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>Decides which source to fetch next.</summary>
/// <remarks>
/// Two kinds, and the detection source comes first: nothing can be judged or cut until it is
/// here, where an added source is only ever one more sample (ADR 0019).
/// </remarks>
public sealed class DownloadSelector(
    JobStore jobs,
    WorkingSet workingSet,
    CratesidePaths paths,
    int maxStrikes = DownloadSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 5;

    public bool TryPickNext(QueueView view, out WantedDownload next)
    {
        next = Detection(view) ?? Added(view)!;

        return next is not null;
    }

    /// <summary>A queued track with no audio held for it yet.</summary>
    private WantedDownload? Detection(QueueView view) =>
        view
            .Outstanding.Where(item =>
                !item.IsUnavailable
                && !workingSet.TryGetSource(item.VideoId, out _)
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
                        && !AwaitingHandoff(added)
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
    private bool AwaitingHandoff(AddedSource added) =>
        new[] { paths.StemsKeyIdentified, paths.StemsUnresolvedKey }
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, $"*({added.Label}).*"))
            .Any();

    /// <remarks>
    /// retrySucceeded is true: the checks above already confirmed the file is gone, and both
    /// the working set and staging may be emptied by hand at any time.
    /// </remarks>
    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Download, maxStrikes, retrySucceeded: true);
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

        var into = wanted.Into ?? _paths.DestinationFor(order.Destination);
        var result = await runner.RunAsync(
            wanted.Url,
            into,
            order.Recipe.Format,
            cancellationToken
        );

        var error = result.IsSuccess ? Placed(wanted, result) : result.Error;

        jobs.Record(
            wanted.VideoId,
            JobStage.Download,
            error is null ? JobStatus.Succeeded : JobStatus.Failed,
            error: error
        );

        return StageTick.Ran(order, error is null, error);
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
