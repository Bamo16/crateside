using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Microsoft.Extensions.Options;

namespace Crateside.Core.WorkLists;

/// <summary>
/// Video IDs crateside has separation to redo for, whatever the playlist says, decided per song
/// (ADR 0014).
/// </summary>
public delegate IReadOnlySet<string> ReadReopenedTracks(KeyLedger ledger);

/// <summary>
/// What the separator can make, and what it calls each one.
/// </summary>
public delegate Task<PresetCatalog> ReadPresetCatalog(CancellationToken ct);

public sealed class WorkListService(
    IOptions<CratesidePaths> paths,
    PlaylistSettingsStore settings,
    TrackInstructionStore instructions,
    PlaylistReader reader,
    PlaylistAuthorizer authorizer,
    PlaylistRemover remover,
    BumpedTrack bumped,
    IntakeStore intake,
    ReadReopenedTracks? reopened = null,
    ReadReopenedTracks? awaitingCut = null,
    ReadPresetCatalog? presets = null,
    ActionLog? actions = null
)
{
    /// <summary>How long a playlist read is reused before going back to the API.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private readonly CratesidePaths _paths = paths.Value;
    private readonly SemaphoreSlim _readGate = new(1, 1);

    private IReadOnlyList<WorkItem>? _cachedItems;
    private DateTimeOffset _cachedAt;

    /// <summary>
    /// When the playlist was last fetched from the API — not when the disk was last scanned — or
    /// null if it never has been.
    /// </summary>
    public DateTimeOffset? PlaylistReadAt => _cachedItems is null ? null : _cachedAt;

    /// <summary>
    /// Why the playlist could not be read, or null when the last read worked. A report, not a
    /// failure: the rows are still built from what crateside has already taken on.
    /// </summary>
    public string? PlaylistProblem { get; private set; }

    /// <summary>Whether an OAuth client exists, so deletion is possible at all.</summary>
    public bool CanAuthorize => authorizer.IsConfigured;

    /// <summary>Whether consent is currently held. False is an ordinary state, not an error.</summary>
    public bool IsAuthorized => authorizer.IsConfigured && authorizer.HasStoredToken;

    /// <summary>Runs the interactive consent flow. Only from an explicit user action.</summary>
    public Task ConsentAsync(CancellationToken ct = default) => authorizer.ConsentAsync(ct);

    /// <summary>Which channel the stored consent belongs to, or null if unknown.</summary>
    public Task<string?> GetAuthorizedChannelAsync(CancellationToken ct = default) =>
        authorizer.GetAuthorizedChannelAsync(ct);

    public Task SignOutAsync() => authorizer.SignOutAsync();

    /// <summary>Whether an API key is available. Without one, nothing can be read.</summary>
    public bool HasCredential => reader.HasCredential;

    /// <summary>The playlist currently being read, or null if none is set yet.</summary>
    public string? PlaylistId => settings.Read().PlaylistId;

    public bool IsConfigured => HasCredential && PlaylistId is { Length: > 0 };

    /// <summary>
    /// Points the Queue at a different playlist. Accepts a bare ID or a pasted URL, and
    /// persists outside the working tree.
    /// </summary>
    public void SetPlaylist(string? playlistIdOrUrl)
    {
        settings.SavePlaylistId(playlistIdOrUrl);

        // A different playlist entirely, so the cached one is not merely stale but wrong.
        Invalidate();
    }

    /// <summary>How often crateside looks at the playlist unprompted.</summary>
    public int RefreshMinutes =>
        settings.Read().RefreshMinutes ?? PlaylistSettings.DefaultRefreshMinutes;

    /// <summary>Sets it. Zero turns it off.</summary>
    public void SetRefreshMinutes(int minutes) => settings.SaveRefreshMinutes(minutes);

    /// <summary>Whether crateside empties the playlist as it takes tracks on (ADR 0021).</summary>
    public bool ConsumeOnArrival => settings.Read().ConsumeOnArrival;

    /// <summary>Sets it. The first scan after turning it on removes everything already known.</summary>
    public void SetConsumeOnArrival(bool consume) => settings.SaveConsumeOnArrival(consume);

    /// <summary>Puts a track at the front of the work lists a stage picks from.</summary>
    public void Bump(string videoId) => bumped.Bump(videoId);

    /// <summary>Gives up the bump, putting the queue back in the playlist's own order.</summary>
    public void ClearBump() => bumped.Clear();

    /// <summary>The track the user asked for next, or null if nobody has.</summary>
    public string? BumpedVideoId => bumped.VideoId;

    /// <summary>Records that the user takes this upload's audio as it comes (#31).</summary>
    public void AcceptUpload(string videoId, string upload) =>
        instructions.AcceptUpload(videoId, upload);

    /// <summary>Records which files the user wants made for a track.</summary>
    public void SetPlan(string videoId, StemPlan? plan) => instructions.SetPlan(videoId, plan);

    /// <summary>Entries crateside tried to empty from the playlist and could not.</summary>
    public IReadOnlyList<IntakeRecord> LeftInPlaylist =>
        [.. intake.Read().Where(record => record.RemovalFailure is not null)];

    /// <summary>Offers an entry back on the next scan, spending another removal on it.</summary>
    public void RetryRemoval(string videoId) => intake.ClearRemovalAttempted([videoId]);

    /// <summary>The standing instruction for a track, or null when there is none.</summary>
    public TrackInstruction? InstructionFor(string videoId) =>
        instructions
            .Read()
            .FirstOrDefault(t => t.VideoId.Equals(videoId, StringComparison.Ordinal));

    /// <summary>Forgets the cached playlist, so the next read goes to the API.</summary>
    public void Invalidate()
    {
        _readGate.Wait();

        try
        {
            _cachedItems = null;
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>
    /// The playlist, from the API or from the last read; readers arriving together share one call.
    /// </summary>
    private async Task<IReadOnlyList<WorkItem>> ItemsAsync(CancellationToken ct)
    {
        await _readGate.WaitAsync(ct);

        try
        {
            if (_cachedItems is { } cached && DateTimeOffset.UtcNow - _cachedAt < CacheFor)
                return cached;

            try
            {
                // Oldest-added first: the only ordering that means the same thing regardless of
                // whether the playlist adds new videos to the top.
                _cachedItems =
                [
                    .. (await reader.ListAsync(PlaylistId ?? string.Empty, ct)).OrderBy(item =>
                        item.AddedAt ?? DateTimeOffset.MaxValue
                    ),
                ];
                _cachedAt = DateTimeOffset.UtcNow;
                PlaylistProblem = null;

                return _cachedItems;
            }
            catch (PlaylistUnavailableException ex)
            {
                // A playlist that cannot be read means no new arrivals, not no queue (ADR 0021).
                // Everything already taken on is on disk, in the ledger and in the intake, and
                // none of it needs the network — so failing the scan here stopped crateside
                // working on tracks it had fully recorded. A spent daily quota did exactly that.
                PlaylistProblem = ex.Message;

                return _cachedItems ?? [];
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>
    /// Reads the queue and keeps everything the read produced. The most expensive thing in the
    /// app — it opens every audio file under three roots — so it must not run per event.
    /// </summary>
    public async Task<QueueScan> ScanAsync(CancellationToken ct = default)
    {
        var held = Pasted(await ItemsAsync(ct));
        var (items, arrivals) = TakenOn(held);

        if (PlaylistProblem is null)
            intake.ForgetRemovalFailures(
                LeftInPlaylist
                    .Select(record => record.VideoId)
                    .Except(held.Select(item => item.VideoId), StringComparer.Ordinal)
            );

        await ConsumeAsync(held, ct);

        // Both staging and the library count — a track that moved out is still processed.
        var ledger = KeyLedger.Read(_paths.Ledger);
        var processed = ProcessedTracks.Scan(ledger, [_paths.StagingFolder, _paths.LibraryFolder]);

        // Deliberately not counted as processed: that would let the janitor discard the
        // working set. It does mean the GPU has already run this track.
        var parked = ProcessedTracks.Scan(ledger, [_paths.StemsUnresolvedKey]);

        NoteFiledArrivals(arrivals, processed, ledger);

        var byVideoId = instructions.Read().ToDictionary(t => t.VideoId, StringComparer.Ordinal);

        var catalog = presets is null ? PresetCatalog.Empty : await presets(ct);

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
                awaitingCut?.Invoke(ledger),
                InventoriesBySong(processed, ledger, catalog, _paths.LibraryFolder)
            ),
            Ledger = ledger,
            Processed = processed,
            Instructions = byVideoId,
            ReadAt = PlaylistReadAt,
            PlaylistProblem = PlaylistProblem,
        };
    }

    /// <summary>The playlist, plus the tracks the user pasted in (ADR 0010).</summary>
    private IReadOnlyList<WorkItem> Pasted(IReadOnlyList<WorkItem> playlist)
    {
        var held = playlist.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. playlist,
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
    /// Gives back the entries crateside has recorded, so the playlist stops holding what it has
    /// already delivered (ADR 0021).
    /// </summary>
    private async Task ConsumeAsync(IReadOnlyList<WorkItem> held, CancellationToken ct)
    {
        // Not "recorded by this scan": a track recorded before the setting was turned on would
        // then never be new again, and the playlist would never empty at all.
        // Never on a failed read: what is held is then stale or empty, and an entry offered
        // back is offered only once.
        if (
            !settings.Read().ConsumeOnArrival
            || !IsAuthorized
            || !intake.IsDurable
            || PlaylistProblem is not null
        )
            return;

        // Marked first, and regardless of what the removal reports. A delete costs fifty quota
        // units against a daily ten thousand, so a failure retried on every scan spends the day
        // rather than reporting a fault — which is what it did.
        var entries = intake.ClaimForRemoval(held, DateTimeOffset.UtcNow);

        if (entries.Count is 0)
            return;

        var offered = entries.Select(item => item.VideoId).ToList();

        try
        {
            var results = await RemoveAsync(entries, ct);
            var videoIdOf = entries.ToDictionary(
                item => item.PlaylistItemId!,
                item => item.VideoId,
                StringComparer.Ordinal
            );

            // Kept on the record rather than discarded: nothing will try these again, so this is
            // the only trace that they were left in the playlist, and why (#34).
            intake.RecordRemovalFailures(
                results
                    .Where(result =>
                        !result.Removed && videoIdOf.ContainsKey(result.PlaylistItemId)
                    )
                    .ToDictionary(
                        result => videoIdOf[result.PlaylistItemId],
                        result => new RemovalFailure
                        {
                            Reason = result.Reason ?? "the API gave no reason",
                            CanSucceedLater = result.CanSucceedLater,
                        },
                        StringComparer.Ordinal
                    )
            );
        }
        catch (PlaylistUnavailableException)
        {
            // Consent has lapsed, so no delete was attempted and no quota was spent. Swallowed
            // rather than thrown: the playlist read itself worked, and failing the scan here
            // stopped every stage on a write the stages do not need (ADR 0026).
            intake.ClearRemovalAttempted(offered);
        }
    }

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
        if (reopened?.Invoke(ledger) is not { Count: > 0 } videoIds)
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

        return await RemoveAsync(view.FullyProcessed, ct);
    }

    /// <summary>
    /// Removes specific items the user picked by hand, whatever the automatic predicate would
    /// claim.
    /// </summary>
    public async Task<IReadOnlyList<RemovalResult>> RemoveAsync(
        IReadOnlyList<WorkItem> items,
        CancellationToken ct = default
    )
    {
        var results = await remover.RemoveAsync(items, ct);

        // The playlist just changed underneath the cache, so holding the old one would show
        // removed tracks as still queued — and a drain loop would pick one.
        Invalidate();

        return results;
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
