using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Puts a track into the work lists without the playlist (ADR 0010).
/// </summary>
public sealed class TrackAdd(
    LedgerStore store,
    TrackInstructionStore instructions,
    SourceProbe probe,
    ResolvedNameStore names
)
{
    /// <summary>What adding this track would do, or why it cannot be done.</summary>
    public AddTrackPlan PlanFor(string? url, IReadOnlyList<TrackRowFacts> known) =>
        PlanFor(url, known, store.Read());

    private static AddTrackPlan PlanFor(
        string? url,
        IReadOnlyList<TrackRowFacts> known,
        KeyLedger ledger
    )
    {
        YouTubeUrl.TryGetVideoId(url, out var videoId);

        return videoId switch
        {
            null => new AddTrackPlan
            {
                Refusal = "That is not a YouTube video ID or URL crateside can read.",
            },
            // A library-only row means the files are here and nothing ever judged them, which is
            // the one case where adding an upload crateside "has" is the point rather than a
            // duplicate. Separation skips it: the inventory already satisfies the plan.
            _ when Holder(known, videoId) is { IsLibraryOnly: true, HasVerdict: false } holder
                    && !IsAddedSource(holder, videoId) => new AddTrackPlan
            {
                VideoId = videoId,
                Song = ledger.SongFor(videoId),
                JudgesWhatIsHere = true,
            },
            _ when Holder(known, videoId) is not null => new AddTrackPlan
            {
                Refusal = "crateside already has that upload.",
            },
            _ => new AddTrackPlan { VideoId = videoId, Song = ledger.SongFor(videoId) },
        };
    }

    private static TrackRowFacts? Holder(IReadOnlyList<TrackRowFacts> known, string videoId) =>
        known.FirstOrDefault(track => track.Uploads.Contains(videoId, StringComparer.Ordinal));

    /// <summary>
    /// Whether this upload is one the row merely carries, and so must never be judged as a track
    /// of its own (ADR 0019).
    /// </summary>
    private static bool IsAddedSource(TrackRowFacts holder, string videoId) =>
        holder.Plan.AddedSources.Any(source =>
            source.VideoId.Equals(videoId, StringComparison.Ordinal)
        );

    /// <summary>
    /// Queues every library upload nothing ever judged, to be judged in the weekly spend-down and
    /// never cut (#47, ADR 0063). Their audio is taken as it comes: the library already uses those files.
    /// </summary>
    public int QueueUnjudged(IReadOnlyList<TrackRowFacts> known)
    {
        var unjudged = known
            .Where(track => track is { IsLibraryOnly: true, HasVerdict: false, HasUpload: true })
            .Select(track => track.VideoId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var videoId in unjudged)
        {
            instructions.AddByUrl(videoId);
            instructions.AcceptUpload(videoId, videoId);
            instructions.MarkBackfill(videoId);
        }

        return unjudged.Length;
    }

    /// <summary>
    /// Records the track, having said what its audio is first so a poor upload is the user's to
    /// accept (ADR 0019).
    /// </summary>
    public async Task<AddTrackOutcome> ApplyAsync(
        string? url,
        IReadOnlyList<TrackRowFacts> known,
        bool anyway = false,
        CancellationToken ct = default
    )
    {
        var plan = PlanFor(url, known, store.Read());

        if (plan.Refusal is { } refusal)
            return new AddTrackOutcome { Problem = refusal };

        var probed = await probe.ReadAsync($"https://music.youtube.com/watch?v={plan.VideoId}", ct);

        if (!probed.IsReadable)
            return new AddTrackOutcome { Problem = probed.Problem };

        if (probed.FallsShort && !anyway)
            return new AddTrackOutcome { Shortfall = probed };

        instructions.AddByUrl(plan.VideoId!);

        // The probe already resolved the name, so keeping it is what stops a pasted row reading
        // as its own video ID until the name worker gets round to asking the same question.
        if (probed is { Title: { Length: > 0 } title })
            names.Save(
                plan.VideoId!,
                new ResolvedName
                {
                    DisplayTitle = title,
                    BaseName = probed.BaseName is { Length: > 0 } file ? file : title,
                }
            );

        return new AddTrackOutcome { VideoId = plan.VideoId, Title = probed.Title };
    }
}

/// <summary>What adding a track would do. Read before it happens, to say so on screen.</summary>
public sealed record AddTrackPlan
{
    public string? VideoId { get; init; }

    /// <summary>What the ledger already calls this video, when it has seen it.</summary>
    public string? Song { get; init; }

    public string? Refusal { get; init; }

    /// <summary>
    /// Adding this buys a verdict for files the library already holds, not a separation.
    /// </summary>
    public bool JudgesWhatIsHere { get; init; }

    public bool IsPossible => Refusal is null && VideoId is not null;
}

/// <summary>What adding a track actually did.</summary>
public sealed record AddTrackOutcome
{
    public string? VideoId { get; init; }

    public string? Title { get; init; }

    /// <summary>What the upload offers, when that is why nothing was added.</summary>
    public ProbedSource? Shortfall { get; init; }

    public string? Problem { get; init; }

    public bool Succeeded => Problem is null && Shortfall is null;
}
