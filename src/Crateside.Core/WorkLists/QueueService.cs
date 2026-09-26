using Crateside.Core.Ledger;
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

public sealed class QueueService(
    IOptions<CratesidePaths> paths,
    QueueSettingsStore settings,
    PlaylistReader reader,
    PlaylistAuthorizer authorizer,
    PlaylistRemover remover,
    BumpedTrack bumped,
    IntakeStore intake,
    ReadReopenedTracks? reopened = null,
    ReadReopenedTracks? awaitingCut = null,
    ReadPresetCatalog? presets = null
)
{
    /// <summary>How long a playlist read is reused before going back to the API.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private readonly CratesidePaths _paths = paths.Value;
    private readonly SemaphoreSlim _readGate = new(1, 1);

    private IReadOnlyList<QueueItem>? _cachedItems;
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
        settings.Read().RefreshMinutes ?? QueueSettings.DefaultRefreshMinutes;

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
        settings.AcceptUpload(videoId, upload);

    /// <summary>Records which files the user wants made for a track.</summary>
    public void SetPlan(string videoId, StemPlan? plan) => settings.SetPlan(videoId, plan);

    /// <summary>Entries crateside tried to empty from the playlist and could not.</summary>
    public IReadOnlyList<IntakeRecord> LeftInPlaylist =>
        [.. intake.Read().Where(record => record.RemovalFailure is not null)];

    /// <summary>Offers an entry back on the next scan, spending another removal on it.</summary>
    public void RetryRemoval(string videoId) => intake.ClearRemovalAttempted([videoId]);

    /// <summary>The standing instruction for a track, or null when there is none.</summary>
    public TrackInstruction? InstructionFor(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(t => t.VideoId.Equals(videoId, StringComparison.Ordinal));

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
    private async Task<IReadOnlyList<QueueItem>> ItemsAsync(CancellationToken ct)
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
            catch (QueueUnavailableException ex)
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
        var items = TakenOn(held);

        await ConsumeAsync(held, ct);

        // Both staging and Mashup Samples count — a track that moved out is still processed.
        var ledger = KeyLedger.Read(_paths.Ledger);
        var processed = ProcessedTracks.Scan(ledger, [_paths.SampleStaging, _paths.MashupSamples]);

        // Deliberately not counted as processed: that would let the janitor discard the
        // working set. It does mean the GPU has already run this track.
        var parked = ProcessedTracks.Scan(ledger, [_paths.StemsUnresolvedKey]);

        var instructions = settings
            .Read()
            .Tracks.ToDictionary(t => t.VideoId, StringComparer.Ordinal);

        var catalog = presets is null ? PresetCatalog.Empty : await presets(ct);

        return new QueueScan
        {
            View = QueueView.Compose(
                items,
                processed.FullyProcessed,
                processed.VerdictWithoutStems,
                items
                    .Where(i => parked.StemsFor(i.VideoId).Count > 0)
                    .Select(i => i.VideoId)
                    .ToHashSet(StringComparer.Ordinal),
                instructions,
                Reopened(items, ledger),
                bumped.VideoId,
                awaitingCut?.Invoke(ledger),
                InventoriesBySong(processed, ledger, catalog)
            ),
            Ledger = ledger,
            Processed = processed,
            Instructions = instructions,
            ReadAt = PlaylistReadAt,
            PlaylistProblem = PlaylistProblem,
        };
    }

    /// <summary>The playlist, plus the tracks the user pasted in (ADR 0010).</summary>
    private IReadOnlyList<QueueItem> Pasted(IReadOnlyList<QueueItem> playlist)
    {
        var held = playlist.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. playlist,
            .. settings
                .Read()
                .Tracks.Where(track => track.AddedByUrl && !held.Contains(track.VideoId))
                .Select(track => new QueueItem { VideoId = track.VideoId, Title = track.VideoId }),
        ];
    }

    /// <summary>
    /// Everything crateside has taken on: what the playlist holds now, plus what it held once
    /// (ADR 0021).
    /// </summary>
    private IReadOnlyList<QueueItem> TakenOn(IReadOnlyList<QueueItem> held)
    {
        var owed = Owed().ToHashSet(StringComparer.Ordinal);

        intake.Record(held);
        intake.Backfill(owed);
        intake.Prune(owed, held.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal));

        return IntakeStore.Merge(held, intake.Read());
    }

    /// <summary>
    /// Tracks the playlist may no longer hold whose instruction still asks for something; an
    /// empty one is a finished track's residue, and owing it would re-download it.
    /// </summary>
    private IEnumerable<string> Owed() =>
        settings
            .Read()
            .Tracks.Where(track =>
                track.Plan is { } plan && (plan.KeepSource || plan.AddedSources.Count > 0)
            )
            .Select(track => track.VideoId);

    /// <summary>
    /// Gives back the entries crateside has recorded, so the playlist stops holding what it has
    /// already delivered (ADR 0021).
    /// </summary>
    private async Task ConsumeAsync(IReadOnlyList<QueueItem> held, CancellationToken ct)
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

        var offerable = intake
            .Read()
            .Where(record => record.RemovalAttemptedAt is null)
            .Select(record => record.VideoId)
            .ToHashSet(StringComparer.Ordinal);

        var entries = held.Where(item =>
                item.PlaylistItemId is not null && offerable.Contains(item.VideoId)
            )
            .ToList();

        if (entries.Count is 0)
            return;

        // Marked first, and regardless of what the removal reports. A delete costs fifty quota
        // units against a daily ten thousand, so a failure retried on every scan spends the day
        // rather than reporting a fault — which is what it did.
        var offered = entries.Select(item => item.VideoId).ToList();

        intake.MarkRemovalAttempted(offered, DateTimeOffset.UtcNow);

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
        catch (QueueUnavailableException)
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
        PresetCatalog catalog
    )
    {
        string Song(string videoId) => ledger.SongFor(videoId) ?? videoId;

        var bySong = processed
            .StemsByVideoId.GroupBy(entry => Song(entry.Key), StringComparer.Ordinal)
            .ToDictionary(
                song => song.Key,
                song => StemInventory.Of(song.SelectMany(entry => entry.Value), catalog),
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
    /// <see cref="QueueView.AwaitingSeparation"/>, never <see cref="QueueView.Outstanding"/>.
    /// </summary>
    private IReadOnlyList<QueueItem> Reopened(
        IReadOnlyList<QueueItem> items,
        Ledger.KeyLedger ledger
    )
    {
        if (reopened?.Invoke(ledger) is not { Count: > 0 } videoIds)
            return [];

        var queued = items.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);
        var held = intake.Read().Select(record => record.VideoId).ToHashSet(StringComparer.Ordinal);

        // Bare, with no instruction fields copied on: QueueView.Compose describes these the same
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
                        ? new QueueItem { VideoId = videoId, Title = run.DisplayName }
                        : null
                )
                .OfType<QueueItem>(),
        ];
    }

    /// <summary>
    /// The queue sorted into work lists — the conclusion of a <see cref="ScanAsync"/>.
    /// </summary>
    public async Task<QueueView> ReadAsync(CancellationToken ct = default) =>
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
        IReadOnlyList<QueueItem> items,
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
    public required QueueView View { get; init; }

    /// <summary>Why the playlist could not be read, or null when it could.</summary>
    public string? PlaylistProblem { get; init; }

    /// <summary>The ledger as it stood for this read, so a second reader gets the same verdicts.</summary>
    public required KeyLedger Ledger { get; init; }

    /// <summary>
    /// Every file found under <c>Sample Staging</c> and <c>Mashup Samples</c>, by video ID. Which
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
