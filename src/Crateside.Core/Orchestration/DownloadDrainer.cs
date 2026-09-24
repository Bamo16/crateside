using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Decides which source to acquire next: audio to cut from, a kept source, or an added one (ADR 0019).
/// </summary>
public sealed class DownloadSelector(
    JobStore jobs,
    WorkingSet workingSet,
    CratesidePaths paths,
    int maxStrikes = DownloadSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 5;

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

        // An owed handoff is what a re-cut asking for the source leaves behind. Without one, a
        // track finished with owes nothing unasked (ADR 0027).
        if (
            IsSweptUp(item.VideoId) && !StageEligibility.Owes(jobs, item.VideoId, JobStage.Handoff)
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

    /// <summary>Retries even a succeeded download, since every caller has already found its file gone.</summary>
    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Download, maxStrikes, retrySucceeded: true);

    /// <summary>
    /// The audio is absent because the janitor took it after separation succeeded, not because it
    /// was never fetched.
    /// </summary>
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

    /// <summary>What the user said an added source is. Null for any other download.</summary>
    public SampleType? Type { get; init; }

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
    /// Puts the fetched file where the pipeline expects it, recording its quality against its own
    /// video ID so a poor added source never parks the track (ADR 0019).
    /// </summary>
    private string? Placed(WantedDownload wanted, DownloadResult result)
    {
        workingSet.SaveAcquisition(wanted.VideoId, result.Acquisition!);

        if (wanted.Label is not { } label)
            return null;

        if (!TrySuffix(result.Path!, label, out var suffixed, out var problem))
            return problem;

        if (wanted.Type is not { } type || SampleTyping.TryWrite(suffixed, type, out var untyped))
            return null;

        // Removed so the retry fetches it again rather than staging it untyped.
        try
        {
            File.Delete(suffixed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return $"downloaded, but could not be marked {type}: {untyped}";
    }

    /// <summary>
    /// Renames a fetched source to carry its label, which the handoff then preserves while it
    /// swaps the track-name half.
    /// </summary>
    private static bool TrySuffix(
        string path,
        string label,
        out string renamed,
        [NotNullWhen(false)] out string? problem
    )
    {
        renamed = Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(path)} ({label}){Path.GetExtension(path)}"
        );

        try
        {
            File.Move(path, renamed, overwrite: true);
            problem = null;

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"downloaded, but could not be named ({label}): {e.Message}";

            return false;
        }
    }
}
