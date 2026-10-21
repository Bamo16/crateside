using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Microsoft.Extensions.Options;

namespace Crateside.Core.WorkLists;

public sealed class WorkListService(
    LedgerStore store,
    IOptions<CratesidePaths> paths,
    TrackInstructionStore instructions,
    BumpedTrack bumped,
    IntakeStore intake,
    CachedPlaylist playlist,
    PlaylistConsumer consumer,
    IReopenedWork? reopened = null,
    PresetCatalogReader? presets = null,
    ActionLog? actions = null
)
{
    private readonly CratesidePaths _paths = paths.Value;

    /// <summary>
    /// Reads the queue and keeps everything the read produced. The most expensive thing in the
    /// app — it opens every audio file under three roots — so it must not run per event.
    /// </summary>
    public async Task<QueueScan> ScanAsync(CancellationToken ct = default)
    {
        var held = Pasted(await playlist.ReadAsync(ct));
        var (items, arrivals) = TakenOn(held);

        if (playlist.Problem is null)
            intake.ForgetRemovalFailures(
                intake
                    .LeftInPlaylist.Select(record => record.VideoId)
                    .Except(held.Select(item => item.VideoId), StringComparer.Ordinal)
            );

        await consumer.ConsumeAsync(held, ct);

        // Both staging and the library count — a track that moved out is still processed.
        var ledger = store.Read();
        var processed = ProcessedTracks.Scan(ledger, [_paths.StagingFolder, _paths.LibraryFolder]);

        // Deliberately not counted as processed: that would let the janitor discard the
        // working set. It does mean the GPU has already run this track.
        var parked = ProcessedTracks.Scan(ledger, [_paths.StemsUnresolvedKey]);

        NoteFiledArrivals(arrivals, processed, ledger);

        var byVideoId = instructions.Read().ToDictionary(t => t.VideoId, StringComparer.Ordinal);

        var catalog = presets is null ? PresetCatalog.Empty : await presets.ReadAsync(ct);

        return new QueueScan
        {
            View = WorkListsView.Compose(
                items,
                processed.FullyProcessed,
                processed.VerdictWithoutStems,
                items
                    .Where(i => parked.StemsFor(i.VideoId).Count > 0)
                    .Select(i => i.VideoId)
                    .ToHashSet(StringComparer.Ordinal),
                byVideoId,
                Reopened(items, ledger),
                bumped.VideoId,
                reopened?.AwaitingCut(ledger),
                InventoriesBySong(processed, ledger, catalog, _paths.LibraryFolder)
            ),
            Ledger = ledger,
            Processed = processed,
            Instructions = byVideoId,
            ReadAt = playlist.ReadAt,
            PlaylistProblem = playlist.Problem,
        };
    }

    /// <summary>The playlist, plus the tracks the user pasted in (ADR 0010).</summary>
    private IReadOnlyList<WorkItem> Pasted(IReadOnlyList<WorkItem> listed)
    {
        var held = listed.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. listed,
            .. instructions
                .Read()
                .Where(track => track.AddedByUrl && !held.Contains(track.VideoId))
                .Select(track => new WorkItem { VideoId = track.VideoId, Title = track.VideoId }),
        ];
    }

    /// <summary>
    /// Everything crateside has taken on: what the playlist holds now, plus what it held once
    /// (ADR 0021).
    /// </summary>
    private (IReadOnlyList<WorkItem> Items, IReadOnlyList<WorkItem> Arrivals) TakenOn(
        IReadOnlyList<WorkItem> held
    )
    {
        var owed = Owed().ToHashSet(StringComparer.Ordinal);

        // A first read takes on the whole playlist at once; none of it is news.
        var isFirstRead = intake.Read().Count is 0;
        var arrivals = intake.Arrive(held);

        intake.Backfill(owed);
        intake.Prune(owed, held.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal));

        return (IntakeStore.Merge(held, intake.Read()), isFirstRead ? [] : arrivals);
    }

    /// <summary>
    /// Tells each arrival's activity that its song is already filed, since nothing else would
    /// show that the add worked (#84).
    /// </summary>
    private void NoteFiledArrivals(
        IReadOnlyList<WorkItem> arrivals,
        ProcessedTracks processed,
        KeyLedger ledger
    )
    {
        if (actions is null)
            return;

        foreach (var arrival in arrivals)
            if (FiledWhere(arrival, processed, ledger, _paths.LibraryFolder) is { } where)
                actions.Record(arrival.VideoId, UserActionKind.ArrivedAlreadyFiled, where);
    }

    /// <summary>
    /// Where the arrival's song already has files, by song rather than upload, or null when it has
    /// none. A song known only by a lookalike name is said to look like one.
    /// </summary>
    internal static string? FiledWhere(
        WorkItem arrival,
        ProcessedTracks processed,
        KeyLedger ledger,
        string finishedRoot
    )
    {
        var known = ledger.SongFor(arrival.VideoId);
        var song =
            known
            ?? (
                Lookalikes.TryFind(arrival.Title, ledger, out var lookalike) ? lookalike.Song : null
            );

        var stems = processed
            .StemsByVideoId.Where(entry =>
                entry.Key.Equals(arrival.VideoId, StringComparison.Ordinal)
                || (
                    song is not null
                    && string.Equals(
                        ledger.SongFor(entry.Key),
                        song,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            .SelectMany(entry => entry.Value)
            .ToList();

        if (stems is [])
            return null;

        var place = stems.Any(stem =>
            stem.Root.Equals(finishedRoot, StringComparison.OrdinalIgnoreCase)
        )
            ? "the library"
            : "Staging";

        return known is null && processed.StemsFor(arrival.VideoId) is []
            ? $"looks like one in {place}"
            : $"in {place}";
    }

    /// <summary>
    /// Tracks the playlist may no longer hold whose instruction still asks for something; an
    /// empty one is a finished track's residue, and owing it would re-download it.
    /// </summary>
    private IEnumerable<string> Owed() =>
        instructions
            .Read()
            .Where(track =>
                track.Plan is { } plan && (plan.KeepSource || plan.AddedSources.Count > 0)
            )
            .Select(track => track.VideoId);

    /// <summary>
    /// What each video ID already has, counting every upload of the same song — including those
    /// with no file of their own (ADR 0014).
    /// </summary>
    internal static IReadOnlyDictionary<string, StemInventory> InventoriesBySong(
        ProcessedTracks processed,
        Ledger.KeyLedger ledger,
        PresetCatalog catalog,
        string? finishedRoot = null
    )
    {
        string Song(string videoId) => ledger.SongFor(videoId) ?? videoId;

        var bySong = processed
            .StemsByVideoId.GroupBy(entry => Song(entry.Key), StringComparer.Ordinal)
            .ToDictionary(
                song => song.Key,
                song =>
                    StemInventory.Of(song.SelectMany(entry => entry.Value), catalog, finishedRoot),
                StringComparer.Ordinal
            );

        return ledger
            .Runs.Select(run => run.SourceVideoId)
            .OfType<string>()
            .Concat(processed.StemsByVideoId.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(videoId => bySong.ContainsKey(Song(videoId)))
            .ToDictionary(
                videoId => videoId,
                videoId => bySong[Song(videoId)],
                StringComparer.Ordinal
            );
    }

    /// <summary>
    /// Judged tracks with separation to redo that the playlist no longer holds — so they reach
    /// <see cref="WorkListsView.AwaitingSeparation"/>, never <see cref="WorkListsView.Outstanding"/>.
    /// </summary>
    private IReadOnlyList<WorkItem> Reopened(IReadOnlyList<WorkItem> items, Ledger.KeyLedger ledger)
    {
        if (reopened?.Owing(ledger) is not { Count: > 0 } videoIds)
            return [];

        var queued = items.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);
        var held = intake.Read().Select(record => record.VideoId).ToHashSet(StringComparer.Ordinal);

        // Bare, with no instruction fields copied on: WorkListsView.Compose describes these the same
        // way it describes playlist items.
        return
        [
            .. videoIds
                .Where(videoId => !queued.Contains(videoId))
                // A track the user removed owes nothing, whatever its jobs say (ADR 0027). No
                // records at all is an intake that could not be read, not one that holds nothing.
                .Where(videoId => held.Count is 0 || held.Contains(videoId))
                .Select(videoId =>
                    ledger.TryGetLatestByVideoId(videoId, out var run)
                        ? new WorkItem { VideoId = videoId, Title = run.DisplayName }
                        : null
                )
                .OfType<WorkItem>(),
        ];
    }

    /// <summary>
    /// The queue sorted into work lists — the conclusion of a <see cref="ScanAsync"/>.
    /// </summary>
    public async Task<WorkListsView> ReadAsync(CancellationToken ct = default) =>
        (await ScanAsync(ct)).View;

    /// <summary>
    /// Removes every fully-processed item from the playlist, re-testing first so a stale page
    /// cannot delete something that has since stopped qualifying.
    /// </summary>
    public async Task<IReadOnlyList<RemovalResult>> RemoveProcessedAsync(
        CancellationToken ct = default
    )
    {
        var view = await ReadAsync(ct);

        return await playlist.RemoveAsync(view.FullyProcessed, ct);
    }
}

/// <summary>
/// One reading of the queue: the work lists, and the material they were derived from.
/// </summary>
public sealed record QueueScan
{
    public required WorkListsView View { get; init; }

    /// <summary>Why the playlist could not be read, or null when it could.</summary>
    public string? PlaylistProblem { get; init; }

    /// <summary>The ledger as it stood for this read, so a second reader gets the same verdicts.</summary>
    public required KeyLedger Ledger { get; init; }

    /// <summary>
    /// Every file found under <c>Staging</c> and the library, by video ID. Which
    /// root a file turned up in is what says whether the user has finished with it (ADR 0007).
    /// </summary>
    public required ProcessedTracks Processed { get; init; }

    /// <summary>
    /// The user's standing instructions by video ID, carried whole because they stay true after
    /// the track leaves the playlist.
    /// </summary>
    public required IReadOnlyDictionary<string, TrackInstruction> Instructions { get; init; }

    /// <summary>When the playlist itself was last fetched. Null if it never has been.</summary>
    public DateTimeOffset? ReadAt { get; init; }
}
