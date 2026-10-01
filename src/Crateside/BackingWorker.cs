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
    IOptions<CratesidePaths> paths,
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

            await Task.Delay(delay, stoppingToken);
        }
    }

    private TimeSpan Tick()
    {
        var ledger = KeyLedger.Read(_paths.Ledger);
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

        var library = Report(runner.Run(_paths.MashupSamples, ledger, OwnedOf), "the library");
        var staging = Report(
            runner.Run(_paths.SampleStaging, ledger, OwnedOf, backKeys: false),
            "Sample Staging"
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
