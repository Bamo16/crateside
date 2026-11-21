namespace Crateside.Core.Orchestration.Handoff;

/// <summary>Cuts a worker's wait short: <see cref="Arm"/> returns what the next <see cref="Set"/> completes.</summary>
public sealed class WakeSignal
{
    private TaskCompletionSource _current = New();

    public Task Arm()
    {
        var next = New();

        Volatile.Write(ref _current, next);

        return next.Task;
    }

    public void Set() => Volatile.Read(ref _current).TrySetResult();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
