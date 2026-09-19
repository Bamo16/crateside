using Crateside.Core.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Queue;

/// <summary>Video IDs crateside has separation to redo for, whatever the playlist says.</summary>
/// <remarks>Takes the ledger because the answer is per song, not per video ID (ADR 0014).</remarks>
public delegate IReadOnlySet<string> ReadReopenedTracks(KeyLedger ledger);

/// <summary>
/// What the separator can make, and what it calls each one.
/// </summary>
public delegate Task<PresetCatalog> ReadPresetCatalog(CancellationToken cancellationToken);

public sealed class QueueService(
    IOptions<CratesidePaths> paths,
    QueueSettingsStore settings,
    YouTubeQueueReader reader,
    QueueAuthorizer authorizer,
    QueueRemover remover,
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
    /// When the playlist was last actually fetched, or null if it never has been.
    /// </summary>
    /// <remarks>
    /// The API read, not the disk scan — the two run on different clocks: a scan happens on
    /// every read while the playlist itself is cached for <see cref="CacheFor"/>.
    /// </remarks>
    public DateTimeOffset? PlaylistReadAt => _cachedItems is null ? null : _cachedAt;

    /// <summary>
    /// Why the playlist could not be read, or null when the last read worked.
    /// </summary>
    /// <remarks>
    /// A report rather than a failure: the rows are still built, from what crateside has already
    /// taken on. Says so on the page, because silence would read as an empty playlist.
    /// </remarks>
    public string? PlaylistProblem { get; private set; }

    /// <summary>Whether an OAuth client exists, so deletion is possible at all.</summary>
    public bool CanAuthorize => authorizer.IsConfigured;

    /// <summary>Whether consent is currently held. False is an ordinary state, not an error.</summary>
    public bool IsAuthorized => authorizer.IsConfigured && authorizer.HasStoredToken;

    /// <summary>Runs the interactive consent flow. Only from an explicit user action.</summary>
    public Task ConsentAsync(CancellationToken cancellationToken = default) =>
        authorizer.ConsentAsync(cancellationToken);

    /// <summary>Which channel the stored consent belongs to, or null if unknown.</summary>
    public Task<string?> GetAuthorizedChannelAsync(CancellationToken cancellationToken = default) =>
        authorizer.GetAuthorizedChannelAsync(cancellationToken);

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

    /// <summary>Records which files the user wants made for a track.</summary>
    public void SetPlan(string videoId, StemPlan? plan) => settings.SetPlan(videoId, plan);

    /// <summary>The standing instruction for a track, or null when there is none.</summary>
    public TrackInstruction? InstructionFor(string videoId) =>
        settings
            .Read()
            .Tracks.FirstOrDefault(t => t.VideoId.Equals(videoId, StringComparison.Ordinal));

    /// <summary>Forgets the cached playlist, so the next read goes to the API.</summary>
    /// <remarks>
    /// For the Refresh button and for anything that changes the playlist itself. Everything
    /// else is content to see a two-minute-old list.
    /// </remarks>
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
    /// The playlist, from the API or from the last read.
    /// </summary>
    /// <remarks>
    /// Gated so that several readers arriving at once make one call between them rather than
    /// one each.
    /// </remarks>
    private async Task<IReadOnlyList<QueueItem>> ItemsAsync(CancellationToken cancellationToken)
    {
        await _readGate.WaitAsync(cancellationToken);

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
                    .. (
                        await reader.ListAsync(PlaylistId ?? string.Empty, cancellationToken)
                    ).OrderBy(item => item.AddedAt ?? DateTimeOffset.MaxValue),
                ];
                _cachedAt = DateTimeOffset.UtcNow;
                PlaylistProblem = null;

                return _cachedItems;
            }
            catch (QueueUnavailableException e)
            {
                // A playlist that cannot be read means no new arrivals, not no queue (ADR 0021).
                // Everything already taken on is on disk, in the ledger and in the intake, and
                // none of it needs the network — so failing the scan here stopped crateside
                // working on tracks it had fully recorded. A spent daily quota did exactly that.
                PlaylistProblem = e.Message;

                return _cachedItems ?? [];
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>
    /// Reads the queue and keeps everything the read produced, not just the conclusion.
    /// </summary>
    /// <remarks>
    /// The most expensive thing in the app — a ledger read plus two recursive walks that open
    /// every audio file under three roots. Must not run per event.
    /// </remarks>
    public async Task<QueueScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        var held = Pasted(await ItemsAsync(cancellationToken));
        var items = TakenOn(held);

        await ConsumeAsync(held, cancellationToken);

        // Both staging and Mashup Samples count — a track that moved out is still processed.
        var ledger = KeyLedger.Read(_paths.Ledger);
        var processed = ProcessedTracks.Scan(ledger, [_paths.SampleStaging, _paths.MashupSamples]);

        // Deliberately not counted as processed: that would let the janitor discard the
        // working set. It does mean the GPU has already run this track.
        var parked = ProcessedTracks.Scan(ledger, [_paths.StemsUnresolvedKey]);

        var instructions = settings
            .Read()
            .Tracks.ToDictionary(t => t.VideoId, StringComparer.Ordinal);

        var catalog = presets is null ? PresetCatalog.Empty : await presets(cancellationToken);

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

    /// <summary>
    /// The playlist, plus the tracks the user pasted in.
    /// </summary>
    /// <remarks>
    /// These join <see cref="QueueView.Items"/> and therefore <see cref="QueueView.Outstanding"/>,
    /// which a re-cut is gated out of (ADR 0010).
    /// </remarks>
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
    /// Everything crateside has taken on: what the playlist holds now, plus what it held once.
    /// </summary>
    /// <remarks>
    /// The playlist is an arrival signal and nothing else (ADR 0021).
    /// </remarks>
    private IReadOnlyList<QueueItem> TakenOn(IReadOnlyList<QueueItem> held)
    {
        var owed = Owed().ToHashSet(StringComparer.Ordinal);

        intake.Record(held);
        intake.Backfill(owed);
        intake.Prune(owed, held.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal));

        return IntakeStore.Merge(held, intake.Read());
    }

    /// <summary>
    /// Tracks the playlist may no longer hold that crateside still owes something for.
    /// </summary>
    /// <remarks>
    /// Only instructions that ask for something: an entry that says nothing is residue of a
    /// finished track, and putting it back in the work lists re-downloads it.
    /// </remarks>
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
    /// <remarks>
    /// Every record not yet offered back, not only this scan's. Off until the user asks: the first
    /// run offers all of them at fifty quota units each, and a deletion is final.
    /// </remarks>
    private async Task ConsumeAsync(
        IReadOnlyList<QueueItem> held,
        CancellationToken cancellationToken
    )
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
        intake.MarkRemovalAttempted(entries.Select(item => item.VideoId), DateTimeOffset.UtcNow);

        await RemoveAsync(entries, cancellationToken);
    }

    /// <summary>
    /// What each video ID already has, counting every upload of the same song (ADR 0014).
    /// </summary>
    /// <remarks>
    /// Keyed by every upload the ledger files under the song, not only those carrying a file of
    /// their own: a second upload of a song already cut has nothing on disk to be found by.
    /// </remarks>
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
    /// Tracks with separation to redo that the playlist no longer holds.
    /// </summary>
    /// <remarks>
    /// Gated to tracks the ledger has already judged, so these never reach
    /// <see cref="QueueView.Outstanding"/> — only <see cref="QueueView.AwaitingSeparation"/>.
    /// </remarks>
    private IReadOnlyList<QueueItem> Reopened(
        IReadOnlyList<QueueItem> items,
        Ledger.KeyLedger ledger
    )
    {
        if (reopened?.Invoke(ledger) is not { Count: > 0 } videoIds)
            return [];

        var queued = items.Select(item => item.VideoId).ToHashSet(StringComparer.Ordinal);

        // Bare, with no instruction fields copied on: QueueView.Compose describes these the same
        // way it describes playlist items.
        return
        [
            .. videoIds
                .Where(videoId => !queued.Contains(videoId))
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
    /// <remarks>
    /// Throws <see cref="QueueUnavailableException"/> if the playlist cannot be read — the
    /// caller must render that as its own state, never as an empty queue.
    /// </remarks>
    public async Task<QueueView> ReadAsync(CancellationToken cancellationToken = default) =>
        (await ScanAsync(cancellationToken)).View;

    /// <summary>
    /// Removes every fully-processed item from the playlist.
    /// </summary>
    /// <remarks>
    /// Re-reads and re-tests before deleting — a stale page must never be able to delete
    /// something that has since stopped qualifying.
    /// </remarks>
    public async Task<IReadOnlyList<RemovalResult>> RemoveProcessedAsync(
        CancellationToken cancellationToken = default
    )
    {
        var view = await ReadAsync(cancellationToken);

        return await RemoveAsync(view.FullyProcessed, cancellationToken);
    }

    /// <summary>
    /// Removes specific items the user picked by hand, whatever the automatic predicate would
    /// claim.
    /// </summary>
    /// <remarks>Does no qualification check: this is the user's explicit call.</remarks>
    public async Task<IReadOnlyList<RemovalResult>> RemoveAsync(
        IReadOnlyList<QueueItem> items,
        CancellationToken cancellationToken = default
    )
    {
        var results = await remover.RemoveAsync(items, cancellationToken);

        // The playlist just changed underneath the cache, so holding the old one would show
        // removed tracks as still queued — and a drain loop would pick one.
        Invalidate();

        return results;
    }
}

/// <summary>
/// One reading of the queue: the work lists, and the material they were derived from.
/// </summary>
/// <remarks>Kept together because recomputing any of it separately means walking the library again.</remarks>
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
    /// The user's standing instructions, by video ID.
    /// </summary>
    /// <remarks>
    /// Carried whole, not only applied to the work lists — a track leaves the playlist once
    /// processed while its instructions (such as having no vocals) go on being true.
    /// </remarks>
    public required IReadOnlyDictionary<string, TrackInstruction> Instructions { get; init; }

    /// <summary>When the playlist itself was last fetched. Null if it never has been.</summary>
    public DateTimeOffset? ReadAt { get; init; }
}
