using Crateside.Core.Tracks;

namespace Crateside;

/// <summary>
/// Wakes an unattended worker as soon as the tracks view shows it something it has not seen, rather
/// than when its idle delay runs out. Each key wakes it once, so a lookup that keeps failing does not.
/// </summary>
public sealed class NewWork : IDisposable
{
    private readonly TracksService _tracks;
    private readonly Func<IReadOnlyList<TrackRow>, IEnumerable<string>> _pending;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="pending">The keys of everything the worker still owes, from the rows as they are.</param>
    public NewWork(TracksService tracks, Func<IReadOnlyList<TrackRow>, IEnumerable<string>> pending)
    {
        _tracks = tracks;
        _pending = pending;
        _tracks.Changed += OnChanged;
    }

    /// <summary>Waits out the delay, or less if something new turns up meanwhile.</summary>
    public async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        Task wake;

        lock (_gate)
            wake = _wake.Task;

        await Task.WhenAny(Task.Delay(delay, ct), wake);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_wake.Task.IsCompleted)
                _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Dispose() => _tracks.Changed -= OnChanged;

    private void OnChanged()
    {
        if (_tracks.Current?.Rows is not { } rows)
            return;

        lock (_gate)
        {
            var added = false;

            foreach (var key in _pending(rows))
                added |= _seen.Add(key);

            if (added)
                _wake.TrySetResult();
        }
    }
}
