using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Points a track at a better upload of the same song, and puts it back through separation.
/// </summary>
public sealed class TrackSwap(
    QueueSettingsStore settings,
    JobStore jobs,
    WorkingSet workingSet,
    ReadStagedFiles staged,
    CratesidePaths paths,
    string uv = "uv"
)
{
    /// <summary>What swapping this track's source would do, or why it cannot be done.</summary>
    public SwapPlan PlanFor(string videoId, string? url) =>
        PlanFor(videoId, url, KeyLedger.Read(paths.Ledger));

    private SwapPlan PlanFor(string videoId, string? url, KeyLedger ledger)
    {
        YouTubeUrl.TryGetVideoId(url, out var newVideoId);

        return (newVideoId, ledger.TryGetLatestByVideoId(videoId, out _)) switch
        {
            (null, _) => Impossible("That is not a YouTube video ID or URL crateside can read."),
            (var id, _) when id.Equals(videoId, StringComparison.Ordinal) => Impossible(
                "That is already this track's source."
            ),
            (_, false) => Impossible(
                "This track has never been judged, so there is no verdict to carry across."
            ),
            _ => new SwapPlan
            {
                NewVideoId = newVideoId,
                Url = $"https://music.youtube.com/watch?v={newVideoId}",
                Removing = staged(videoId),
            },
        };
    }

    /// <summary>
    /// Tells keytag the new video is this song, records the URL, throws away the old audio, and
    /// reopens separation.
    /// </summary>
    /// <remarks>The restatement must land first, or stems cut from the new video resolve to nothing.</remarks>
    public async Task<SwapOutcome> ApplyAsync(
        string videoId,
        string url,
        CancellationToken cancellationToken = default
    )
    {
        var ledger = KeyLedger.Read(paths.Ledger);
        var plan = PlanFor(videoId, url, ledger);

        if (plan.Refusal is { } refusal)
            return new SwapOutcome { Problem = refusal };

        if (
            await RestateAsync(videoId, plan.NewVideoId!, plan.Url!, ledger, cancellationToken) is
            { } failure
        )
            return new SwapOutcome { Problem = failure };

        settings.SetSourceUrl(videoId, plan.Url);

        var removed = 0;

        foreach (var stem in plan.Removing)
        {
            try
            {
                File.Delete(stem.Path);
                File.Delete(StemFileName.AnalysisFor(stem.Path));

                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new SwapOutcome
                {
                    NewVideoId = plan.NewVideoId,
                    Removed = removed,
                    Problem =
                        $"{stem.FileName} could not be removed — it is probably open in Ableton. "
                        + "Close it and press again.",
                };
            }
        }

        // The retained download is the old upload, and separation prefers it over the URL.
        workingSet.Discard(videoId);

        foreach (var stage in StemRecut.Redone)
            jobs.Record(videoId, stage, JobStatus.Cancelled, error: "source swapped");

        return new SwapOutcome { NewVideoId = plan.NewVideoId, Removed = removed };
    }

    /// <summary>Has keytag record that the new video is this song, or says why it could not.</summary>
    private async Task<string?> RestateAsync(
        string videoId,
        string newVideoId,
        string url,
        KeyLedger ledger,
        CancellationToken cancellationToken
    )
    {
        // keytag refuses a URL the song already has, so a retry must skip rather than fail.
        if (
            ledger.TryGetLatestByVideoId(videoId, out var current)
            && ledger.TryGetLatestByVideoId(newVideoId, out var already)
            && already.Song.Equals(current.Song, StringComparison.OrdinalIgnoreCase)
        )
            return null;

        var outcome = await ExternalProcess.RunAsync(
            uv,
            [
                "run",
                "--project",
                paths.KeytagRepo,
                "keytag-restate",
                "--from",
                videoId,
                "--to",
                url,
            ],
            workingDirectory: null,
            cancellationToken
        );

        return outcome.Succeeded
            ? null
            : ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                ?? $"keytag-restate exited with {outcome.ExitCode}";
    }

    private static SwapPlan Impossible(string refusal) =>
        new() { Removing = [], Refusal = refusal };
}

/// <summary>What a swap would do. Read before it happens, to say so on screen.</summary>
public sealed record SwapPlan
{
    /// <summary>The video the track would be cut from, or null when the URL was unreadable.</summary>
    public string? NewVideoId { get; init; }

    /// <summary>The canonical watch URL for <see cref="NewVideoId"/>, which is what keytag is given.</summary>
    public string? Url { get; init; }

    /// <summary>The staged files that would go: they came from the upload being replaced.</summary>
    public required IReadOnlyList<StagingStem> Removing { get; init; }

    /// <summary>Why this cannot happen, or null when it can.</summary>
    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null;

    /// <summary>The sentence beside the button, saying what pressing it costs.</summary>
    public const string Detail =
        "keytag records the new video as this song, so the key and BPM carry across and nothing "
        + "is judged again. Separation downloads the replacement and runs from it.";
}

/// <summary>What a swap actually did.</summary>
public sealed record SwapOutcome
{
    public string? NewVideoId { get; init; }

    /// <summary>Staged files removed.</summary>
    public int Removed { get; init; }

    /// <summary>What went wrong, or null when nothing did.</summary>
    public string? Problem { get; init; }

    public bool Succeeded => Problem is null;
}
