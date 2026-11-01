using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.Samples;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Orchestration.Download;

/// <summary>Fetches one source at a time, for as long as any is owed.</summary>
public sealed class DownloadDrainer(
    LedgerStore store,
    WorkListService queue,
    DownloadRunner runner,
    DownloadSelector selector,
    JobStore jobs,
    WorkingSet workingSet,
    IOptions<CratesidePaths> paths
) : IStageDrainer
{
    private readonly CratesidePaths _paths = paths.Value;

    public JobStage Stage => JobStage.Download;

    public async Task<StageTick> DrainOnceAsync(CancellationToken ct = default)
    {
        WorkListsView view;

        try
        {
            view = await queue.ReadAsync(ct);
        }
        catch (PlaylistUnavailableException ex)
        {
            return StageTick.Unavailable(ex.Message);
        }

        if (!selector.TryPickNext(view, out var wanted))
            return StageTick.Idle;

        var ledger = store.Read();
        var order = WorkOrder.Compose(wanted.Track, ledger);

        jobs.Record(wanted.VideoId, JobStage.Download, JobStatus.Running);

        var destination = _paths.DestinationFor(order.Destination);

        var (error, summary) = wanted.Copy
            ? Copied(wanted.VideoId, destination)
            : await FetchedAsync(wanted, order, destination, ct);

        jobs.Record(
            wanted.VideoId,
            JobStage.Download,
            error is null ? JobStatus.Succeeded : JobStatus.Failed,
            error: error,
            summary: error is null ? summary : null
        );

        return StageTick.Ran(order, error is null, error);
    }

    private async Task<(string? Error, string? Summary)> FetchedAsync(
        WantedDownload wanted,
        WorkOrder order,
        string destination,
        CancellationToken ct
    )
    {
        var result = await runner.RunAsync(
            wanted.Url,
            wanted.Into ?? destination,
            order.Recipe.Format,
            ct
        );

        return result.IsSuccess ? (Placed(wanted, result), result.Summary) : (result.Error, null);
    }

    /// <summary>
    /// Whether the source is already a sample in Staging or the library. A second copy in the stem
    /// folder makes the next handoff refuse to move anything, guide included.
    /// </summary>
    internal static bool IsFiled(CratesidePaths paths, string fileName) =>
        File.Exists(Path.Combine(paths.StagingFolder, fileName))
        || (
            Directory.Exists(paths.LibraryFolder)
            && Directory
                .EnumerateFiles(paths.LibraryFolder, fileName, SearchOption.AllDirectories)
                .Any()
        );

    /// <summary>
    /// Copies the separation source into the destination, for a plan that keeps it as a sample.
    /// </summary>
    private (string? Error, string? Summary) Copied(string videoId, string destination)
    {
        if (!workingSet.TryGetSource(videoId, out var held))
            return ("the audio to copy is no longer on disk", null);

        if (IsFiled(_paths, Path.GetFileName(held)))
            return (null, "kept the source as a sample, which was already filed");

        try
        {
            Directory.CreateDirectory(destination);
            File.Copy(held, Path.Combine(destination, Path.GetFileName(held)), overwrite: true);

            return (null, "kept the source as a sample");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ($"the source could not be copied: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Puts the fetched file where the pipeline expects it, recording its quality against its own
    /// video ID so a poor added source never parks the track (ADR 0019).
    /// </summary>
    private string? Placed(WantedDownload wanted, DownloadResult result)
    {
        workingSet.SaveAcquisition(
            wanted.VideoId,
            result.Acquisition! with
            {
                IsAccepted = IsAccepted(wanted),
            }
        );

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return $"downloaded, but could not be marked {type}: {untyped}";
    }

    /// <summary>
    /// Whether the user already took this upload's audio as it comes, before this download of it
    /// landed (#31).
    /// </summary>
    internal static bool IsAccepted(WantedDownload wanted) =>
        wanted is { IsAdded: false, Track.AcceptedUpload: { } accepted }
        && YouTubeUrl.TryGetVideoId(wanted.Url, out var upload)
        && upload.Equals(accepted, StringComparison.Ordinal);

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
        renamed = StemFileName.Labeled(path, label);

        try
        {
            File.Move(path, renamed, overwrite: true);
            problem = null;

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"downloaded, but could not be named ({label}): {ex.Message}";

            return false;
        }
    }
}
