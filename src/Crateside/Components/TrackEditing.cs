namespace Crateside.Components;

/// <summary>
/// Which of a track's rename and swap boxes is open, shared by the two places that mount them so
/// only one is ever open.
/// </summary>
public sealed class TrackEditing
{
    public event Action? Changed;

    public TrackEdit Open { get; private set; }

    public void Start(TrackEdit edit)
    {
        Open = edit;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (Open is TrackEdit.None)
            return;

        Open = TrackEdit.None;
        Changed?.Invoke();
    }
}

public enum TrackEdit
{
    None,
    Rename,
    Swap,
}
