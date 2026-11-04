using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Tracks;

namespace Crateside;

/// <summary>Takes in what lands in the inbox, a few seconds after it stops growing (ADR 0077).</summary>
public sealed class InboxWorker(
    InboxScanner scanner,
    CratesidePaths paths,
    StageSignals signals,
    TracksService tracks,
    ILogger<InboxWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Directory.CreateDirectory(paths.Inbox);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The inbox {Inbox} could not be created", paths.Inbox);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scanning the inbox failed");

                await Task.Delay(Interval, stoppingToken);
            }
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        var scan = scanner.Scan();

        foreach (var file in scan.Left)
            logger.LogInformation(
                "{File} in the inbox is not audio crateside takes in; left alone",
                file
            );

        foreach (var outcome in scan.Outcomes)
            switch (outcome)
            {
                case IntakeOutcome.TakenIn(var key, var name):
                    logger.LogInformation("Took in {Name} from the inbox as {TrackKey}", name, key);

                    break;
                case IntakeOutcome.AlreadyHeld(var key, var name):
                    logger.LogInformation("{Name} ({TrackKey}) was already taken in", name, key);

                    break;
                case IntakeOutcome.Refused(var reason):
                    logger.LogWarning("The inbox could not take a file in: {Reason}", reason);

                    break;
            }

        if (scan.Outcomes.Any(outcome => outcome is IntakeOutcome.TakenIn))
        {
            signals.Wake(JobStage.Download, JobStage.Evidence);
            await tracks.RefreshAsync(ct);
        }
    }
}
