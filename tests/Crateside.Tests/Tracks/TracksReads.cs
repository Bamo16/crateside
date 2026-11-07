using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>A reader whose every read is the next view the test hands it.</summary>
internal sealed class TracksReads(Func<TracksView> next) : ITracksReader
{
    public Task<TracksView> ReadAsync(CancellationToken ct = default) => Task.FromResult(next());
}
