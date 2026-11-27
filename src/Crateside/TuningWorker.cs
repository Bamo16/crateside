using Crateside.Core.Tuning;
using Crateside.Core.Watching;
using Humanizer;

namespace Crateside;

/// <summary>
/// Reads each track's tuning and writes the detune that brings it to A = 440, when the app starts, when Staging or the library changes and every ten minutes besides.
/// Never paused: keydetect runs on the CPU and contends for nothing (ADR 0101).
/// </summary>
public sealed class TuningWorker(
    TuningTracks tracks,
    TuningPass pass,
    FileChanges changes,
    ILogger<TuningWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly FileChanges.Listener _wake = changes.Listen();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (
                    await pass.RunAsync(
                        await Task.Run(tracks.Find, stoppingToken),
                        stoppingToken
                    ) is
                    { Count: > 0 } detuned
                )
                    logger.LogInformation(
                        "Set the detune of {Count}",
                        "song".ToQuantity(detuned.Count)
                    );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Detuning songs failed");
            }

            try
            {
                await _wake.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
