namespace Crateside.Core.Tracks;

/// <summary>
/// How the one list is narrowed.
/// </summary>
/// <remarks>
/// Counts are taken over every row, never the filtered set, so the pills say what is out there
/// rather than what is currently shown.
/// </remarks>
public enum TrackFilter
{
    All,

    /// <summary>Staged and tagged: the warping view, and the one the user lives in.</summary>
    InStaging,

    /// <summary>Something automatic is happening, or is about to.</summary>
    InFlight,

    /// <summary>Stopped, parked, or carrying a finding.</summary>
    NeedsAttention,

    /// <summary>Set aside on purpose.</summary>
    Held,

    /// <summary>
    /// Finished and in <c>Mashup Samples</c> — including tracks crateside never queued.
    /// </summary>
    Done,
}

public static class TrackFilters
{
    /// <summary>Every row matches exactly one filter besides <see cref="TrackFilter.All"/>.</summary>
    public static bool Matches(this TrackRow row, TrackFilter filter) =>
        filter switch
        {
            TrackFilter.All => !row.IsLibraryOnly,
            TrackFilter.InStaging => row.State is TrackState.ReadyToWarp,
            TrackFilter.InFlight => row.State
                is TrackState.Queued
                    or TrackState.Detecting
                    or TrackState.Judging
                    or TrackState.JudgedInLine
                    or TrackState.Separating
                    or TrackState.Staging
                    or TrackState.Tagging,
            TrackFilter.NeedsAttention => row.State is TrackState.Parked or TrackState.GaveUp
                || row.AuditNote is not null
                || row.AwaitingRename > 0,
            TrackFilter.Held => row.State is TrackState.Held,
            TrackFilter.Done => row.State is TrackState.Done,
            _ => false,
        };

    /// <summary>How many rows are in each filter, over the whole list.</summary>
    public static int Count(this IEnumerable<TrackRow> rows, TrackFilter filter) =>
        rows.Count(row => row.Matches(filter));
}

/// <summary>How a state chip is drawn. Four colours, each meaning one thing.</summary>
public enum TrackChipStyle
{
    /// <summary>Nothing has happened yet.</summary>
    NeutralOutline,

    /// <summary>The machine is working on it right now.</summary>
    TealFilled,

    /// <summary>The machine will work on it, but is not yet.</summary>
    TealOutline,

    /// <summary>The user's turn.</summary>
    AmberFilled,

    /// <summary>Stopped, and nothing automatic will move it.</summary>
    RedOutline,

    /// <summary>Stopped after trying.</summary>
    RedFilled,

    /// <summary>Finished.</summary>
    GreenOutline,

    /// <summary>Set aside — dashed, because it is a deliberate absence rather than a state.</summary>
    DashedOutline,
}
