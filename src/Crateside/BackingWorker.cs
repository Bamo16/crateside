using System.Collections.Concurrent;
using System.Diagnostics;
using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Handoff;
using Humanizer;
using Microsoft.Extensions.Options;

namespace Crateside;

/// <summary>
/// Keeps the library's keys backed against hand-applied tags (ADR 0023) and every keyed file's
/// genres (ADR 0088) and familiarity (#52) written. Never paused: it contends for nothing.
/// </summary>
public sealed class BackingWorker(
    BackingRunner runner,
    GenreStore genres,
    FamiliarityStore familiarity,
    LiveAnswers answers,
    TrackMarkStore trackMarks,
    MeterStore meters,
    TagStoreBackups backups,
    FileIndexReader index,
    IOptions<CratesidePaths> paths,
    LedgerStore store,
    ILogger<BackingWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(15);

    /// <summary>A stale write lost a race with Ableton, so it is worth retrying promptly.</summary>
    private static readonly TimeSpan AfterStale = TimeSpan.FromSeconds(30);

    private readonly CratesidePaths _paths = paths.Value;

    private readonly WakeSignal _marked = new();

    private readonly ConcurrentQueue<string> _markedSongs = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        trackMarks.Changed += OnMarked;

        try
        {
            await RunAsync(stoppingToken);
        }
        finally
        {
            trackMarks.Changed -= OnMarked;
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var marked = _marked.Arm();
            TimeSpan delay;
            try
            {
                var afterMarks = BackMarked();
                var afterPass = Tick();

                delay = afterMarks < afterPass ? afterMarks : afterPass;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Backing the library failed; trying again later");

                delay = AfterFailure;
            }

            // A mark cuts the wait short, so it lands while the user is looking.
            await WaitAsync(delay, marked, stoppingToken);
        }
    }

    private static async Task WaitAsync(
        TimeSpan delay,
        Task marked,
        CancellationToken stoppingToken
    )
    {
        using var cut = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var waiting = Task.Delay(delay, cut.Token);

        // Canceled rather than abandoned, so the timer does not outlive the wait.
        if (await Task.WhenAny(waiting, marked) == marked)
            await cut.CancelAsync();

        try
        {
            await waiting;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
    }

    private void OnMarked(string song)
    {
        _markedSongs.Enqueue(song);
        _marked.Set();
    }

    private TimeSpan Tick()
    {
        var ledger = store.Read();
        var libraryIndex = ReadLibraryIndex();

        // Before anything is written, so an answer given in Live outranks what this pass would write.
        RecordLiveAnswers(ledger, libraryIndex);
        SettleMeters(ledger, libraryIndex);

        var settled = genres.Read();
        var reach = familiarity.Read();

        // Asked before the write, so a value that changes later is known to have been asked.
        genres.MarkTagged(
            settled
                .Values.Where(record => record.Genres.Except(record.Tagged).Any())
                .Select(record => (record.Song, record.Genres))
        );
        familiarity.MarkTagged(
            reach
                .Values.Where(record => record.Written is not null)
                .Select(record => (record.Song, record.Written!))
        );
        settled = genres.Read();
        reach = familiarity.Read();

        IReadOnlyList<string> RetiredOf(string trackKey)
        {
            var song = ledger.SongFor(trackKey) ?? trackKey;

            return
            [
                .. meters.Retired(song),
                .. settled.GetValueOrDefault(song)?.Retired.Select(GenreVocabulary.Keyword) ?? [],
                .. reach.GetValueOrDefault(song)?.Retired.Select(FamiliarityVocabulary.Keyword)
                    ?? [],
            ];
        }

        IReadOnlyList<string> OwnedOf(string trackKey)
        {
            var song = ledger.SongFor(trackKey) ?? trackKey;

            return
            [
                .. meters.Wanted(song),
                .. settled.GetValueOrDefault(song)?.Genres.Select(GenreVocabulary.Keyword) ?? [],
                .. reach.GetValueOrDefault(song)?.Written is { } known
                    ? [FamiliarityVocabulary.Keyword(known)]
                    : Array.Empty<string>(),
            ];
        }

        var started = Stopwatch.GetTimestamp();

        var library = Report(
            runner.Run(
                _paths.LibraryFolder,
                ledger,
                OwnedOf,
                index: libraryIndex,
                marksOf: MarksOf,
                beforeWrite: backups.Save,
                retiredOf: RetiredOf
            ),
            "the library",
            started
        );

        started = Stopwatch.GetTimestamp();

        var staging = Report(
            runner.Run(
                _paths.StagingFolder,
                ledger,
                OwnedOf,
                backKeys: false,
                retiredOf: RetiredOf
            ),
            "Staging",
            started
        );

        return library < staging ? library : staging;
    }

    private IReadOnlyCollection<TrackMark> MarksOf(string song) =>
        meters.MarksFor(song, trackMarks.Of(song));

    /// <summary>Carries a Meter tag set or taken off on one stem to the song's others (#154).</summary>
    private void SettleMeters(KeyLedger ledger, FolderInfo? libraryIndex)
    {
        var changed = runner
            .MeterReadings(_paths.LibraryFolder, ledger, libraryIndex)
            .Concat(runner.MeterReadings(_paths.StagingFolder, ledger, index: null))
            .GroupBy(stem => stem.Song, StringComparer.OrdinalIgnoreCase)
            .Where(song => meters.Settle(song.Key, [.. song]))
            .ToList();

        foreach (var song in changed)
            logger.LogInformation(
                "Meter of {Song}: {Meter}",
                song.Key,
                meters.Of(song.Key) is { Contradicts: true }
                    ? "the stems contradict"
                    : string.Join(", ", meters.Of(song.Key)?.Applied ?? [])
            );
    }

    private void RecordLiveAnswers(KeyLedger ledger, FolderInfo? libraryIndex)
    {
        var edits = runner
            .LiveEdits(_paths.LibraryFolder, ledger, libraryIndex, answers.WrittenFor)
            .Concat(
                runner.LiveEdits(_paths.StagingFolder, ledger, index: null, answers.WrittenFor)
            );

        var recorded = answers.Record(edits);

        if (recorded > 0)
            logger.LogInformation(
                "Recorded {Answers} from Live as the person's own",
                "answer".ToQuantity(recorded)
            );
    }

    /// <summary>Writes the Collections of each song marked since the last pass, ahead of the whole pass (#144).</summary>
    private TimeSpan BackMarked()
    {
        var delay = IdleDelay;

        if (_markedSongs.IsEmpty || ReadLibraryIndex() is not { } libraryIndex)
            return delay;

        var ledger = store.Read();

        while (_markedSongs.TryDequeue(out var song))
        {
            var started = Stopwatch.GetTimestamp();
            var outcome = runner.Run(
                _paths.LibraryFolder,
                ledger,
                index: libraryIndex,
                marksOf: MarksOf,
                beforeWrite: backups.Save,
                song: song
            );
            var next = Report(outcome, "the library", started);

            if (next < delay)
                delay = next;
        }

        return delay;
    }

    // Null when it cannot be read: read as empty, an imported file would lose its Collections.
    private FolderInfo? ReadLibraryIndex()
    {
        try
        {
            return index.Read(_paths.LibraryFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private TimeSpan Report(TagWriteOutcome outcome, string root, long started)
    {
        switch (outcome)
        {
            case TagWriteOutcome.Written(0):
                return IdleDelay;

            case TagWriteOutcome.Written(var applied):
                logger.LogInformation(
                    "Backed {Applied} entries in {Root} against the tag store in {Elapsed} ms",
                    applied,
                    root,
                    (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
                );

                return IdleDelay;

            case TagWriteOutcome.Stale:
                logger.LogInformation("The tag store moved while backing {Root}; retrying", root);

                return AfterStale;

            case TagWriteOutcome.Rejected(var error):
                logger.LogWarning("Could not back {Root}: {Error}", root, error);

                return AfterFailure;

            default:
                throw new UnreachableException();
        }
    }
}
