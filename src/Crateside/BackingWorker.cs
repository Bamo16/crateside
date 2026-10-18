using System.Diagnostics;
using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Handoff;
using Microsoft.Extensions.Options;

namespace Crateside;

/// <summary>
/// Keeps the library's keys backed against hand-applied tags (ADR 0023) and every keyed file's
/// genre (ADR 0030) and familiarity (#52) written. Never paused: it contends for nothing.
/// </summary>
public sealed class BackingWorker(
    BackingRunner runner,
    GenreStore genres,
    FamiliarityStore familiarity,
    KeyCorrections corrections,
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = Tick();
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

            // An approval cuts the wait short, so a correction lands while the user is looking.
            await corrections.WaitAsync(delay, stoppingToken);
        }
    }

    private TimeSpan Tick()
    {
        var ledger = store.Read();
        var settled = genres.Read();
        var reach = familiarity.Read();

        IReadOnlyList<string> OwnedOf(string videoId)
        {
            var song = ledger.SongFor(videoId) ?? videoId;

            return
            [
                .. settled.GetValueOrDefault(song)?.Written is { } genre
                    ? [GenreVocabulary.Keyword(genre)]
                    : Array.Empty<string>(),
                .. reach.GetValueOrDefault(song)?.Written is { } known
                    ? [FamiliarityVocabulary.Keyword(known)]
                    : Array.Empty<string>(),
            ];
        }

        // Null when it cannot be read: read as empty, an imported file would lose its Collections.
        FolderInfo? libraryIndex;

        try
        {
            libraryIndex = index.Read(_paths.LibraryFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            libraryIndex = null;
        }

        var library = Report(
            runner.Run(
                _paths.LibraryFolder,
                ledger,
                OwnedOf,
                corrections: corrections.Approved(),
                index: libraryIndex,
                beforeWrite: backups.Save
            ),
            "the library"
        );

        // From the index, which the write above reaches minutes later: an approval stays until
        // the index agrees, so a correction is never dropped as done before it shows.
        corrections.Found(
            runner.Disagreements(
                _paths.LibraryFolder,
                ledger,
                libraryIndex ?? FolderInfo.Empty(_paths.LibraryFolder)
            )
        );
        var staging = Report(
            runner.Run(_paths.StagingFolder, ledger, OwnedOf, backKeys: false),
            "Staging"
        );

        return library < staging ? library : staging;
    }

    private TimeSpan Report(TagWriteOutcome outcome, string root)
    {
        switch (outcome)
        {
            case TagWriteOutcome.Written(0):
                return IdleDelay;

            case TagWriteOutcome.Written(var applied):
                logger.LogInformation(
                    "Backed {Applied} entries in {Root} against the tag store",
                    applied,
                    root
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
