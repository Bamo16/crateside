using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>A process table the test controls, so the interlock can be exercised without one.</summary>
internal sealed class FakeProcessProbe : IProcessProbe
{
    public Dictionary<int, DateTimeOffset> Running { get; } = [];

    public bool TryGetStartTime(int processId, out DateTimeOffset startedAt) =>
        Running.TryGetValue(processId, out startedAt);
}
