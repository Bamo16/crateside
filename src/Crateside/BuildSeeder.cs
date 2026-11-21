using Crateside.Core;

namespace Crateside;

/// <summary>Seeds the detector builds in the background, so the app answers while a first-run fetch is still going (#163).</summary>
public sealed class BuildSeeder(KnownBuilds builds, ILogger<BuildSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await builds.StartSeeding(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        foreach (var filter in Enum.GetValues<FfmpegFilter>())
            logger.LogInformation(
                "{Filter} runs on ffmpeg {Build} at {Path}",
                FfmpegBuild.NameOf(filter),
                builds.Pinned(filter)?.Build.Label ?? "(no pinned build)",
                builds.FileNameFor(filter)
            );
    }
}
