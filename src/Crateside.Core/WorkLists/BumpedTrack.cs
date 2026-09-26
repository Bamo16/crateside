namespace Crateside.Core.WorkLists;

/// <summary>
/// The one track the user wants worked next, if they have said so — held in memory only
/// (ADR 0015).
/// </summary>
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
