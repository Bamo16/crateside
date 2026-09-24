using Crateside.Core.Ledger;
using Crateside.Core.Queue;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Attaches another whole file of a song to a track: downloaded, staged and tagged like any
/// sample, and never separated (ADR 0019).
/// </summary>
public sealed class SourceAdd(
    QueueSettingsStore settings,
    CratesidePaths paths,
    SourceProbe probe,
    string uv = "uv"
)
{
    /// <summary>What adding this source would do, or why it cannot be done.</summary>
    public AddPlan PlanFor(TrackRowFacts track, string? url, string? label, SampleType? type) =>
        PlanFor(track, url, label, type, KeyLedger.Read(paths.Ledger));

    private AddPlan PlanFor(
        TrackRowFacts track,
        string? url,
        string? label,
        SampleType? type,
        KeyLedger ledger
    )
    {
        YouTubeUrl.TryGetVideoId(url, out var videoId);

        return (videoId, label?.Trim()) switch
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
            _ when track.Uploads.Contains(videoId, StringComparer.Ordinal) => Impossible(
                "This track already has that upload."
            ),
            _ when OtherSong(ledger, videoId, track.Song) is { } song => Impossible(
                $"keytag has that video as {song}. Adding it here would make the two one song."
            ),
            (_, { } given) => new AddPlan
            {
                Source = new AddedSource
                {
                    VideoId = videoId,
                    Label = given,
                    Type = type,
                },
                CarriesKey = track.Key,
            },
        };
    }

    /// <summary>
    /// Tells keytag the video is this song, then records it on the plan for the download stage
    /// to fetch.
    /// </summary>
    /// <remarks>
    /// The restatement lands first, as it does for a swap: until keytag agrees the two are one
    /// song, the downloaded file arrives as a row of its own rather than as part of this track.
    /// </remarks>
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
        CancellationToken cancellationToken = default
    )
    {
        var plan = PlanFor(track, url, label, type, KeyLedger.Read(paths.Ledger));

        if (plan.Refusal is { } refusal)
            return new AddOutcome { Problem = refusal };

        var source = plan.Source!;
        var probed = await probe.ReadAsync(source.WatchUrl, cancellationToken);

        if (!probed.IsReadable)
            return new AddOutcome { Problem = probed.Problem };

        // Asked before anything is fetched, because the cost of a poor download is not the
        // download: it is a file the library gained that nobody was warned about.
        if (probed.FallsShort && !anyway)
            return new AddOutcome { Shortfall = probed };

        if (await RestateAsync(track.VideoId, source.VideoId, cancellationToken) is { } failure)
            return new AddOutcome { Problem = failure };

        settings.SetPlan(track.VideoId, track.Plan.Adding(source));

        return new AddOutcome { Source = source };
    }

    /// <summary>What the upload is called, for suggesting what kind of file it is.</summary>
    public async Task<string?> TitleOfAsync(string? url, CancellationToken cancellationToken) =>
        YouTubeUrl.TryGetVideoId(url, out var videoId)
            ? (
                await probe.ReadAsync(
                    $"https://music.youtube.com/watch?v={videoId}",
                    cancellationToken
                )
            ).Title
            : null;

    /// <summary>The song keytag already has this video under, when it is not this track's.</summary>
    private static string? OtherSong(KeyLedger ledger, string videoId, string? song) =>
        ledger.SongFor(videoId) is { } known
        && !known.Equals(song, StringComparison.OrdinalIgnoreCase)
            ? known
            : null;

    private async Task<string?> RestateAsync(
        string videoId,
        string added,
        CancellationToken cancellationToken
    )
    {
        var outcome = await ExternalProcess.RunAsync(
            uv,
            [
                "run",
                "--project",
                paths.KeytagRepo,
                "keytag-restate",
                "--from",
                videoId,
                "--to",
                $"https://music.youtube.com/watch?v={added}",
            ],
            workingDirectory: null,
            cancellationToken
        );

        return outcome.Succeeded
            ? null
            : ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                ?? $"keytag-restate exited with {outcome.ExitCode}";
    }

    private static AddPlan Impossible(string refusal) => new() { Refusal = refusal };
}

/// <summary>What a row knows about itself that adding a source needs.</summary>
/// <remarks>
/// Passed rather than re-derived: the row already folded a song's uploads together, and doing
/// it again here would be a second answer to the same question (ADR 0014).
/// </remarks>
public sealed record TrackRowFacts
{
    public required string VideoId { get; init; }

    /// <summary>The ledger identity every upload of this track resolves to.</summary>
    public string? Song { get; init; }

    /// <summary>The key an added source inherits, or null when there is none to inherit.</summary>
    public string? Key { get; init; }

    public required StemPlan Plan { get; init; }

    /// <summary>Every upload this track already speaks for, so none is added twice.</summary>
    public required IReadOnlyList<string> Uploads { get; init; }

    /// <summary>The row exists only because the library holds files for it.</summary>
    public bool IsLibraryOnly { get; init; }

    /// <summary>Whether the ledger has ever judged this track, whatever it decided.</summary>
    /// <remarks>
    /// Not <see cref="Key"/>: a LOW or WITHHELD verdict leaves that null and is still a judgement
    /// that has been made.
    /// </remarks>
    public bool HasVerdict { get; init; }

    /// <summary>Labels already in use, which a new one may not repeat.</summary>
    public IReadOnlyList<string> Labels => [.. Plan.AddedSources.Select(source => source.Label)];
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
