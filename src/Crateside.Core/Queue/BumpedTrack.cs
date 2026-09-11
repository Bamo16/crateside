namespace Crateside.Core.Queue;

/// <summary>
/// The one track the user wants worked next, if they have said so.
/// </summary>
/// <remarks>
/// Deliberately not persisted, and deliberately one at a time. A bump is about the queue in
/// front of you now: one made last week still jumping ahead today would be wrong, and a list of
/// them would be a priority scheme nobody asked for. FIFO is right until the moment it is not
/// (ADR 0015).
/// </remarks>
public sealed class BumpedTrack
{
    private readonly Lock _gate = new();

    private string? _videoId;

    public string? VideoId
    {
        get
        {
            lock (_gate)
                return _videoId;
        }
    }

    /// <summary>Bumping another track replaces the first — there is only ever one.</summary>
    public void Bump(string videoId)
    {
        lock (_gate)
            _videoId = videoId;
    }

    public void Clear()
    {
        lock (_gate)
            _videoId = null;
    }
}
