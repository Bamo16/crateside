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

    /// <summary>A genre with no answer, or a familiarity the numbers leave to the user (#65).</summary>
    TagsToAnswer,

    /// <summary>Not warped yet, and beatdetect reads the tempo drifting: expect a hard warp (#75).</summary>
    TempoDrifts,

    /// <summary>A steady warp whose grid falls away from the audio, which a retype fixes (#96).</summary>
    OffItsTempo,

    /// <summary>
    /// Every row there is, the finished library included. Answers "do I have this track in any
    /// shape or form", which is a search question.
    /// </summary>
    All,
}

public static class TrackFilters
{
    /// <summary>Not a partition: a row can be in two views at once.</summary>
    public static bool Matches(this TrackRow row, TrackFilter filter) =>
        filter switch
        {
            TrackFilter.Unfinished => row.State is not TrackState.Done,
            TrackFilter.InStaging => row.State is TrackState.ReadyToWarp or TrackState.ReadyToMove,
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
            TrackFilter.TagsToAnswer => row.HasTagsToAnswer,
            TrackFilter.TempoDrifts => row.TempoDrifts,
            TrackFilter.OffItsTempo => row.OffItsTempo,
            TrackFilter.All => true,
            _ => false,
        };

    /// <summary>
    /// The rows a view shows: a search looks through every track and ignores familiarity (#142),
    /// and Off its tempo puts the furthest off first.
    /// </summary>
    public static IEnumerable<TrackRow> Narrow(
        this IEnumerable<TrackRow> rows,
        TrackFilter filter,
        string? search = null,
        string? familiarity = null
    )
    {
        if (!string.IsNullOrWhiteSpace(search))
            return rows.Where(row => row.Matches(search));

        var shown = rows.Where(row => row.Matches(filter) && row.HasFamiliarity(familiarity));

        return filter is TrackFilter.OffItsTempo
            ? shown.OrderByDescending(row => Math.Abs(row.Bpm?.Off?.DriftMs ?? 0))
            : shown;
    }

    /// <summary>Whether this row's familiarity, as it is tagged, is <paramref name="value"/>; every row when none is asked for.</summary>
    public static bool HasFamiliarity(this TrackRow row, string? value) =>
        value is null || row.Familiarity?.Written == value;

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

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) is true;
}

/// <summary>How a state chip is drawn. Four colors, each meaning one thing.</summary>
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
