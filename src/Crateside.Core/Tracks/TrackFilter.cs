namespace Crateside.Core.Tracks;

/// <summary>
/// How the one list is narrowed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Filters rather than sections.</strong> The page used to be several lists, so managing
/// one track meant watching it jump between them as its state changed — the row's address on
/// screen moved every time the row's meaning did. A filter narrows one list instead: a track that
/// stops matching disappears from the view the user chose, rather than reappearing somewhere else
/// they have to go and find.
/// </para>
/// <para>
/// Counts are always taken over every row, never over the filtered set, so the pills say what is
/// out there rather than what is currently shown.
/// </para>
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

    /// <summary>Finished and in <c>Mashup Samples</c>.</summary>
    Done,
}

public static class TrackFilters
{
    /// <summary>Whether a row belongs in a filter.</summary>
    /// <remarks>
    /// Every row matches exactly one filter besides <see cref="TrackFilter.All"/>, which is what
    /// lets the counts be read as a partition of the list rather than as overlapping tallies.
    /// </remarks>
    public static bool Matches(this TrackRow row, TrackFilter filter) =>
        filter switch
        {
            TrackFilter.All => true,
            TrackFilter.InStaging => row.State is TrackState.ReadyToWarp,
            TrackFilter.InFlight => row.State
                is TrackState.Queued
                    or TrackState.Detecting
                    or TrackState.Judging
                    or TrackState.JudgedInLine
                    or TrackState.Separating
                    or TrackState.Staging
                    or TrackState.Tagging,
            // A finding counts even when the track is otherwise fine, because a stale tag is
            // invisible everywhere else: the file looks finished and pairs on the wrong key.
            TrackFilter.NeedsAttention => row.State is TrackState.Parked or TrackState.GaveUp
                || row.AuditNote is not null,
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
