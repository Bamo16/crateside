namespace Crateside.Core.WorkLists;

/// <summary>
/// The one track the user wants worked next, if they have said so — held in memory only
/// (ADR 0015).
/// </summary>
public sealed class BumpedTrack
{
    private readonly Lock _gate = new();

    private string? _trackKey;

    public string? TrackKey
    {
        get
        {
            lock (_gate)
                return _trackKey;
        }
    }

    /// <summary>Bumping another track replaces the first — there is only ever one.</summary>
    public void Bump(string trackKey)
    {
        lock (_gate)
            _trackKey = trackKey;
    }

    public void Clear()
    {
        lock (_gate)
            _trackKey = null;
    }
}
