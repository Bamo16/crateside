using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;
using Crateside.Core.Staging;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Points a track at a better upload of the same song, and puts it back through separation.
/// </summary>
public sealed class TrackSwap(
    LedgerStore store,
    TrackInstructionStore instructions,
    WorkingSet workingSet,
    IStagedFiles staged,
    TimeProvider time,
    DeletionLog? deletions = null
)
{
    private readonly DeletionLog _deletions = deletions ?? DeletionLog.None;

    /// <summary>What swapping this track's source would do, or why it cannot be done.</summary>
    public SwapPlan PlanFor(string videoId, string? url) => PlanFor(videoId, url, store.Read());

    private SwapPlan PlanFor(string videoId, string? url, KeyLedger ledger)
    {
        YouTubeUrl.TryGetVideoId(url, out var newVideoId);

        return (newVideoId, ledger.TryGetLatestByVideoId(videoId, out _)) switch
        {
            (null, _) => Impossible("That is not a YouTube video ID or URL crateside can read."),
            (var id, _) when id.Equals(videoId, StringComparison.Ordinal) => Impossible(
                "That is already this track's source."
            ),
            (_, var judged) => new SwapPlan
            {
                NewVideoId = newVideoId,
                Url = $"https://music.youtube.com/watch?v={newVideoId}",
                Removing = staged.Of(videoId),
                CarriesVerdict = judged,
            },
        };
    }

    /// <summary>
    /// Records the new video as this song — first, or its stems resolve to nothing — then
    /// records the URL, throws away the old audio, and reopens separation.
    /// </summary>
    public SwapOutcome Apply(string videoId, string url)
    {
        var ledger = store.Read();
        var plan = PlanFor(videoId, url, ledger);

        if (plan.Refusal is { } refusal)
            return new SwapOutcome { Problem = refusal };

        if (Restate(videoId, plan.Url!, ledger) is { } failure)
            return new SwapOutcome { Problem = failure };

        instructions.SetSourceUrl(videoId, plan.Url);

        var removed = 0;

        foreach (var stem in plan.Removing)
        {
            try
            {
                _deletions.Recycle(stem.Path, "superseded by a source swap");
                RecycleBin.Remove(StemFileName.AnalysisFor(stem.Path));

                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

        instructions.RequestCut(videoId, time.GetUtcNow());

        return new SwapOutcome { NewVideoId = plan.NewVideoId, Removed = removed };
    }

    /// <summary>
    /// Records that the new video is this song, or says why it could not; skipped with no verdict
    /// to carry across (ADR 0016).
    /// </summary>
    private string? Restate(string videoId, string url, KeyLedger ledger) =>
        ledger.HasRunFor(videoId) ? store.Restate(videoId, url).Refusal : null;

    private static SwapPlan Impossible(string refusal) =>
        new() { Removing = [], Refusal = refusal };
}

/// <summary>What a swap would do. Read before it happens, to say so on screen.</summary>
public sealed record SwapPlan
{
    private const string CarriedDetail =
        "The ledger records the new video as this song, so the key and BPM carry across and nothing "
        + "is judged again. Separation downloads the replacement and runs from it.";

    private const string FreshDetail =
        "Nothing has been judged for this track yet, so there is nothing to carry across. The "
        + "downloaded audio is thrown away and the track starts again from the new upload.";

    /// <summary>The video the track would be cut from, or null when the URL was unreadable.</summary>
    public string? NewVideoId { get; init; }

    /// <summary>The canonical watch URL for <see cref="NewVideoId"/>.</summary>
    public string? Url { get; init; }

    /// <summary>The staged files that would go: they came from the upload being replaced.</summary>
    public required IReadOnlyList<StagedFile> Removing { get; init; }

    /// <summary>Whether there is a verdict to carry across to the new upload.</summary>
    public bool CarriesVerdict { get; init; }

    /// <summary>Why this cannot happen, or null when it can.</summary>
    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null;

    /// <summary>The sentence beside the button, saying what pressing it costs.</summary>
    public string Detail => DetailFor(CarriesVerdict);

    /// <summary>The same sentence for a row with no URL typed yet, which has no plan to ask.</summary>
    public static string DetailFor(bool carriesVerdict) =>
        carriesVerdict ? CarriedDetail : FreshDetail;
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
