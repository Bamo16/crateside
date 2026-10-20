namespace Crateside.Core.Watching;

/// <summary>
/// Tells the file workers to look now. A burst of notifications is one wake-up, 2 s after the last
/// (#132); what to do about it stays the worker's scan.
/// </summary>
public sealed class FileChanges(TimeProvider time)
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = time;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private long _version;
    private TaskCompletionSource _fired = NewSignal();

    /// <summary>A worker's view of the wake-ups, which remembers the last one it acted on.</summary>
    public Listener Listen() => new(this);

    public void Notify()
    {
        lock (_gate)
        {
            if (_timer is null)
                _timer = _time.CreateTimer(_ => Fire(), null, Quiet, Timeout.InfiniteTimeSpan);
            else
                _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            _version++;
            _fired.TrySetResult();
            _fired = NewSignal();
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public sealed class Listener
    {
        private readonly FileChanges _changes;
        private long _seen;

        internal Listener(FileChanges changes) => _changes = changes;

        /// <summary>Waits out the timer, or less if something changed since the last wait ended.</summary>
        public async Task WaitAsync(TimeSpan timer, CancellationToken ct)
        {
            Task fired;

            lock (_changes._gate)
            {
                if (_changes._version != _seen)
                {
                    _seen = _changes._version;

                    return;
                }

                fired = _changes._fired.Task;
            }

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await Task.WhenAny(Task.Delay(timer, _changes._time, stop.Token), fired);
            await stop.CancelAsync();
            ct.ThrowIfCancellationRequested();

            lock (_changes._gate)
                _seen = _changes._version;
        }
    }
}
