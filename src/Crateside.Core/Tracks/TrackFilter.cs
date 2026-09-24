namespace Crateside.Core.Tracks;

/// <summary>
/// How the one list is narrowed.
/// </summary>
public enum TrackFilter
{
    /// <summary>
    /// Work that is not finished — the default view, and the union of the four named views below.
    /// </summary>
    Unfinished,

    /// <summary>Staged and tagged: the warping view, and the one the user lives in.</summary>
    InStaging,

    /// <summary>Something automatic is happening, or is about to.</summary>
    InFlight,

    /// <summary>Stopped, parked, or carrying a finding.</summary>
    NeedsAttention,

    /// <summary>
    /// Every row there is, the finished library included. Answers "do I have this track in any
    /// shape or form", which is a search question.
    /// </summary>
    All,
}

public static class TrackFilters
{
    /// <summary>
    /// The filters that name somewhere a track *is*, as against a set that merely contains it.
    /// Every state but <see cref="TrackState.Done"/> falls in exactly one of them.
    /// </summary>
    private static readonly TrackFilter[] Named =
    [
        TrackFilter.InStaging,
        TrackFilter.InFlight,
        TrackFilter.NeedsAttention,
    ];

    /// <summary>Not a partition: a row can be in two views at once.</summary>
    public static bool Matches(this TrackRow row, TrackFilter filter) =>
        filter switch
        {
            TrackFilter.Unfinished => row.State is not TrackState.Done,
            TrackFilter.InStaging => row.State is TrackState.ReadyToWarp,
            TrackFilter.InFlight => row.State
                is TrackState.Queued
                    or TrackState.Downloading
                    or TrackState.Detecting
                    or TrackState.Judging
                    or TrackState.JudgedInLine
                    or TrackState.Separating
                    or TrackState.Staging
                    or TrackState.Tagging,
            TrackFilter.NeedsAttention => row.State
                is TrackState.Parked
                    or TrackState.GaveUp
                    or TrackState.Short
                || row.AuditNote is not null
                || row.Lookalike is not null
                || row.AwaitingRename > 0,
            TrackFilter.All => true,
            _ => false,
        };

    /// <summary>How many rows are in each filter, over the whole list.</summary>
    public static int Count(this IEnumerable<TrackRow> rows, TrackFilter filter) =>
        rows.Count(row => row.Matches(filter));

    /// <summary>
    /// Whether a row answers a search: the name the list shows it under, the name it arrived
    /// with, its key, and its video ID. An empty search matches everything.
    /// </summary>
    public static bool Matches(this TrackRow row, string? search) =>
        search?.Trim() is not { Length: > 0 } term
        || Contains(row.DerivedTitle, term)
        || Contains(row.Title, term)
        || Contains(row.Verdict?.Key, term)
        || Contains(row.VideoId, term);

    /// <summary>
    /// Where else a search would find something: the named views, else <see cref="TrackFilter.All"/>,
    /// never <see cref="TrackFilter.Unfinished"/>.
    /// </summary>
    public static IReadOnlyList<TrackFilter> Holding(
        this IEnumerable<TrackRow> rows,
        string? search
    )
    {
        var matched = rows.Where(row => row.Matches(search)).ToArray();

        if (matched.Length is 0)
            return [];

        TrackFilter[] named = [.. Named.Where(filter => matched.Any(row => row.Matches(filter)))];

        return named.Length > 0 ? named : [TrackFilter.All];
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) is true;
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
