using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;

namespace Crateside;

/// <summary>
/// Asks StemForge what each queued track's files will be called, so the user can decide whether
/// to rename before the GPU runs. Never paused: it contends for nothing.
/// </summary>
public sealed class NameWorker(
    WorkListService queue,
    NameResolver resolver,
    ResolvedNameStore store,
    TracksService tracks,
    ILogger<NameWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Uploads that would not resolve, so a removed or private video is not asked about every
    /// tick for ever. Held in memory only, so a restart retries them.
    /// </summary>
    private readonly HashSet<string> _unresolvable = new(StringComparer.Ordinal);

    private readonly NewWork _newWork = new(
        tracks,
        rows =>
            rows.Where(row => row.NameSource is TrackNameSource.Playlist)
                .Select(row => row.TrackKey)
    );

    public override void Dispose()
    {
        _newWork.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resolving names failed; trying again later");

                delay = AfterFailure;
            }

            await _newWork.WaitAsync(delay, stoppingToken);
        }
    }

    private async Task<TimeSpan> TickAsync(CancellationToken stoppingToken)
    {
        var scan = await queue.ScanAsync(stoppingToken);
        var known = store.Read();

        var unresolved = scan
            .View.Items.Where(item =>
                !known.ContainsKey(item.TrackKey) && !_unresolvable.Contains(item.TrackKey)
            )
            .ToArray();

        if (unresolved.Length is 0)
            return IdleDelay;

        var named = 0;

        // Sequential on purpose: each is a network call, and there is nothing waiting on the
        // answer. Resolving the whole backlog at once would be a burst for no gain.
        foreach (var item in unresolved)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var url = WorkOrder.Compose(item, scan.Ledger).SourceUrl;

            if (await resolver.ResolveAsync(url, stoppingToken) is not { } resolved)
            {
                _unresolvable.Add(item.TrackKey);

                logger.LogInformation("No name resolved for {TrackKey}", item.TrackKey);

                continue;
            }

            store.Save(item.TrackKey, resolved);
            named++;

            logger.LogInformation(
                "{TrackKey} will be called {Name}",
                item.TrackKey,
                resolved.DisplayTitle
            );
        }

        // The rows carry the name, and the genre and familiarity lookups wait for it.
        if (named > 0)
            await tracks.RefreshAsync(stoppingToken);

        return IdleDelay;
    }
}
