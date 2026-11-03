namespace Crateside.Demo;

/// <summary>A clock the generator sets to each moment it writes a record for, backwards included.</summary>
internal sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public void Set(DateTimeOffset at) => _now = at;

    public override DateTimeOffset GetUtcNow() => _now;
}
