using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Plans;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Attaches another whole file of a song to a track: downloaded, staged and tagged like any
/// sample, and never separated (ADR 0019).
/// </summary>
public sealed class SourceAdd(
    LedgerStore store,
    TrackInstructionStore instructions,
    SourceProbe probe
)
{
    /// <summary>What adding this source would do, or why it cannot be done.</summary>
    public AddPlan PlanFor(TrackRowFacts track, string? url, string? label, SampleType? type) =>
        PlanFor(track, url, label, type, store.Read());

    private static AddPlan PlanFor(
        TrackRowFacts track,
        string? url,
        string? label,
        SampleType? type,
        KeyLedger ledger
    )
    {
        YouTubeUrl.TryGetVideoId(url, out var trackKey);

        return (trackKey, label?.Trim()) switch
        {
            (null, _) => Impossible("That is not a YouTube video ID or URL crateside can read."),
            _ when type is null => Impossible("Say whether it is a vocal or an instrumental."),
            (_, not { Length: > 0 }) => Impossible(
                "Give it a name. It becomes the file's suffix, which is what tells it apart."
            ),
            (_, { } given) when !StemFileName.IsUsable(given) => Impossible(
                "That name cannot go on a file."
            ),
            (_, { } given) when track.Labels.Contains(given, StringComparer.OrdinalIgnoreCase) =>
                Impossible($"This track already has a source called {given}."),
            _ when track.Uploads.Contains(trackKey, StringComparer.Ordinal) => Impossible(
                "This track already has that upload."
            ),
            _ when OtherSong(ledger, trackKey, track.Song) is { } song => Impossible(
                $"The ledger has that video as {song}. Adding it here would make the two one song."
            ),
            (_, { } given) => new AddPlan
            {
                Source = new AddedSource
                {
                    TrackKey = trackKey,
                    Label = given,
                    Type = type,
                },
                CarriesKey = track.Key,
            },
        };
    }

    /// <summary>
    /// Records in the ledger that the video is this song, then — never before — records it on the plan for the
    /// download stage to fetch.
    /// </summary>
    /// <param name="anyway">
    /// The user has seen what the audio is and wants it regardless. The ordinary path for an
    /// unofficial upload, not an override.
    /// </param>
    public async Task<AddOutcome> ApplyAsync(
        TrackRowFacts track,
        string? url,
        string? label,
        SampleType? type,
        bool anyway = false,
        CancellationToken ct = default
    )
    {
        var plan = PlanFor(track, url, label, type, store.Read());

        if (plan.Refusal is { } refusal)
            return new AddOutcome { Problem = refusal };

        var source = plan.Source!;
        var probed = await probe.ReadAsync(source.WatchUrl, ct);

        if (!probed.IsReadable)
            return new AddOutcome { Problem = probed.Problem };

        // Asked before anything is fetched, because the cost of a poor download is not the
        // download: it is a file the library gained that nobody was warned about.
        if (probed.FallsShort && !anyway)
            return new AddOutcome { Shortfall = probed };

        if (
            store
                .Restate(track.TrackKey, $"https://music.youtube.com/watch?v={source.TrackKey}")
                .Refusal is
            { } failure
        )
            return new AddOutcome { Problem = failure };

        instructions.SetPlan(track.TrackKey, track.Plan.Adding(source));

        return new AddOutcome { Source = source };
    }

    /// <summary>What the upload is called, for suggesting what kind of file it is.</summary>
    public async Task<string?> TitleOfAsync(string? url, CancellationToken ct) =>
        YouTubeUrl.TryGetVideoId(url, out var trackKey)
            ? (await probe.ReadAsync($"https://music.youtube.com/watch?v={trackKey}", ct)).Title
            : null;

    /// <summary>The song the ledger already has this video under, when it is not this track's.</summary>
    private static string? OtherSong(KeyLedger ledger, string trackKey, string? song) =>
        ledger.SongFor(trackKey) is { } known
        && !known.Equals(song, StringComparison.OrdinalIgnoreCase)
            ? known
            : null;

    private static AddPlan Impossible(string refusal) => new() { Refusal = refusal };
}

/// <summary>What adding a source would do. Read before it happens, to say so on screen.</summary>
public sealed record AddPlan
{
    /// <summary>What would be attached, or null when it cannot be.</summary>
    public AddedSource? Source { get; init; }

    /// <summary>The key it would be tagged with, or null when the track has none yet.</summary>
    public string? CarriesKey { get; init; }

    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null && Source is not null;
}

/// <summary>What adding a source actually did.</summary>
public sealed record AddOutcome
{
    public AddedSource? Source { get; init; }

    /// <summary>What the upload offers, when that is why nothing was added.</summary>
    public ProbedSource? Shortfall { get; init; }

    public string? Problem { get; init; }

    public bool Succeeded => Problem is null && Shortfall is null;
}
