using System.Collections.Concurrent;
using Crateside.Core.Orchestration;

namespace Crateside.Core.Playback;

/// <summary>The full mixes being fetched again for playback, one download each through <c>stemforge-cli</c> (ADR 0094).</summary>
public sealed class MixFetches(SourceBackfill backfill)
{
    private readonly ConcurrentDictionary<string, bool> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);

    /// <summary>Raised when a fetch starts or ends, on whatever thread did it.</summary>
    public event Action? Changed;

    public bool IsFetching(string trackKey) => _active.ContainsKey(trackKey);

    /// <summary>Why the last fetch of this track failed, until the next one starts.</summary>
    public string? FailureOf(string trackKey) => _failures.GetValueOrDefault(trackKey);

    /// <summary>Starts the download and returns at once; false when one is already under way.</summary>
    public bool Start(string trackKey)
    {
        if (!_active.TryAdd(trackKey, true))
            return false;

        _failures.TryRemove(trackKey, out _);
        Changed?.Invoke();
        _ = FetchAsync(trackKey);

        return true;
    }

    private async Task FetchAsync(string trackKey)
    {
        try
        {
            var results = await backfill.RunAsync([trackKey]);

            if (results is [{ Error: { } error }, ..])
                _failures[trackKey] = error;
        }
        finally
        {
            _active.TryRemove(trackKey, out _);
            Changed?.Invoke();
        }
    }
}
