using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside;

/// <summary>
/// Asks StemForge what each queued track's files will be called, so the user can decide whether
/// to rename before the GPU runs rather than after.
/// </summary>
/// <remarks>
/// Not an <see cref="IStageDrainer"/> and deliberately not pausable: it contends for nothing
/// — no GPU, no Claude quota.
/// </remarks>
public sealed class NameWorker(
    QueueService queue,
    NameResolver resolver,
    ResolvedNameStore store,
    ILogger<NameWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Uploads that would not resolve, so a removed or private video is not asked about every
    /// tick for ever.
    /// </summary>
    /// <remarks>Not persisted: a restart is a cheap place to retry a call that may have been unlucky.</remarks>
    private readonly HashSet<string> _unresolvable = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;

            try
            {
                delay = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Resolving names failed; trying again later");

                delay = AfterFailure;
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        var scan = await queue.ScanAsync(stoppingToken);
        var known = store.Read();

        var unresolved = scan
            .View.Items.Where(item =>
                !known.ContainsKey(item.VideoId) && !_unresolvable.Contains(item.VideoId)
            )
            .ToArray();

        if (unresolved.Length is 0)
            return IdleDelay;

        // Sequential on purpose: each is a network call, and there is nothing waiting on the
        // answer. Resolving the whole backlog at once would be a burst for no gain.
        foreach (var item in unresolved)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var url = WorkOrder.Compose(item, scan.Ledger).SourceUrl;

            if (await resolver.ResolveAsync(url, stoppingToken) is not { } resolved)
            {
                _unresolvable.Add(item.VideoId);

                logger.LogInformation("No name resolved for {VideoId}", item.VideoId);

                continue;
            }

            store.Save(item.VideoId, resolved);

            logger.LogInformation(
                "{VideoId} will be called {Name}",
                item.VideoId,
                resolved.DisplayTitle
            );
        }

        return IdleDelay;
    }
}
