using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.Samples;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Attaches another whole file of a song to a track: downloaded, staged and tagged like any
/// sample; only an instrumental's drums are cut, as a warp guide (ADR 0019, ADR 0103).
/// </summary>
public sealed class SourceAdd(
    LedgerStore store,
    TrackInstructionStore instructions,
    IntakeStore intake,
    RejudgeQueue rejudges,
    TrackRename rename,
    IStagedFiles staged,
    ILibraryFiles library,
    SourceProbe probe,
    TimeProvider time
)
{
    /// <summary>What adding this source would do, or why it cannot be done.</summary>
    public AddPlan PlanFor(TrackRowFacts track, string? url, string? label, SampleType? type) =>
        PlanFor(track, url, label, type, store.Read());

    private AddPlan PlanFor(
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
            (_, { } given) when OtherSong(ledger, trackKey, track.Song) is { } other => Folding(
                track,
                new AddedSource
                {
                    TrackKey = trackKey,
                    Label = given,
                    Type = type,
                },
                other
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
    /// The upload is its own song in the ledger: offered as a fold, its whole-source files on disk
    /// becoming the added source's under the added source's name (ADR 0111, rules 31-36).
    /// </summary>
    private AddPlan Folding(TrackRowFacts track, AddedSource source, LedgerRun other)
    {
        if (track.Song is null)
            return Impossible(
                $"The ledger has that video as {other.DisplayName}, and this track has no verdict "
                    + "yet, so there is no song to fold it into."
            );

        var files = WholeSourcesOf(source.TrackKey);
        FileMove[] renaming =
        [
            .. files
                .Select(path => new FileMove(path, NameFor(path, track.Name, source.Label)))
                .Where(move => !move.From.Equals(move.To, StringComparison.OrdinalIgnoreCase)),
        ];

        if (rename.RefusalFor(renaming) is { } refusal)
            return Impossible($"{other.DisplayName} cannot be folded in yet: {refusal}");

        return new AddPlan
        {
            Source = source,
            CarriesKey = track.Key,
            Fold = new SongFold
            {
                Song = other.Song,
                DisplayName = other.DisplayName,
                Files = files,
                Renaming = renaming,
            },
        };
    }

    /// <summary>
    /// Records in the ledger that the video is this song, then — never before — records it on the plan for the
    /// download stage to fetch. A fold renames the files first, so a rename stopped half way changes nothing else.
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

        // A file already on disk is never fetched, so what the upload offers does not matter.
        if (plan.Fold is not { Files.Count: > 0 })
        {
            var probed = await probe.ReadAsync(source.WatchUrl, ct);

            if (!probed.IsReadable)
                return new AddOutcome { Problem = probed.Problem };

            // Asked before anything is fetched, because the cost of a poor download is not the
            // download: it is a file the library gained that nobody was warned about.
            if (probed.FallsShort && !anyway)
                return new AddOutcome { Shortfall = probed };
        }

        if (
            plan.Fold is { Renaming.Count: > 0 } renamed
            && rename.Rename(renamed.Renaming) is { Problem: { } stuck }
        )
            return new AddOutcome { Problem = stuck };

        if (
            store
                .Restate(track.TrackKey, $"https://music.youtube.com/watch?v={source.TrackKey}")
                .Refusal is
            { } failure
        )
            return new AddOutcome { Problem = failure };

        Record(track.TrackKey, track.Plan, source);

        if (plan.Fold is { } fold)
            EndTrackOf(source.TrackKey, fold.Song);

        return new AddOutcome { Source = source, Fold = plan.Fold };
    }

    /// <summary>
    /// Puts the source on the plan. An instrumental also asks for a cut, which is what lets its
    /// drums guide be made once the file is staged; a vocal needs none (ADR 0103).
    /// </summary>
    internal void Record(string trackKey, StemPlan plan, AddedSource source)
    {
        instructions.SetPlan(trackKey, plan.Adding(source));

        if (source.Type is SampleType.Instrumental)
            instructions.RequestCut(trackKey, time.GetUtcNow());
    }

    /// <summary>What the upload is called, for suggesting what kind of file it is.</summary>
    public async Task<string?> TitleOfAsync(string? url, CancellationToken ct) =>
        YouTubeUrl.TryGetVideoId(url, out var trackKey)
            ? (await probe.ReadAsync($"https://music.youtube.com/watch?v={trackKey}", ct)).Title
            : null;

    /// <summary>
    /// Ends the folded upload's life as a track of its own; its song leaves the re-judge queue once
    /// no upload is left to it. Its runs stay in the ledger.
    /// </summary>
    private void EndTrackOf(string upload, string song)
    {
        instructions.Forget(upload);
        intake.Forget(upload);

        if (!store.Read().UploadsOf(song).Any())
            rejudges.Remove(song);
    }

    /// <summary>The upload's files in Staging and the library that are its whole audio, not a stem cut from it.</summary>
    private IReadOnlyList<string> WholeSourcesOf(string upload) =>
        [
            .. staged
                .Of(upload)
                .Where(file => file is { Preset: null, IsAdopted: false })
                .Select(file => file.Path),
            .. library
                .Of(upload)
                .Where(path =>
                    StemTagReader.Read(path)?.Provenance
                        is { Preset: null, Model: null, IsAdopted: false }
                ),
        ];

    /// <summary>The name an added source's file has: the track's name, then its label.</summary>
    private static string NameFor(string path, string? trackName, string label) =>
        StemFileName.Labeled(
            Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty,
                (
                    trackName is { Length: > 0 } name
                        ? StemFileName.FileSafe(name)
                        : StemFileName.WithoutPreset(path)
                ) + Path.GetExtension(path)
            ),
            label
        );

    /// <summary>The latest run of the song the ledger already has this video under, when it is not this track's.</summary>
    private static LedgerRun? OtherSong(KeyLedger ledger, string trackKey, string? song) =>
        ledger.TryGetLatestByTrackKey(trackKey, out var run)
        && !run.Song.Equals(song, StringComparison.OrdinalIgnoreCase)
            ? run
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

    /// <summary>The song the upload stops being, when the ledger has it as one of its own.</summary>
    public SongFold? Fold { get; init; }

    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null && Source is not null;
}

/// <summary>A judged song folded into another track as one of its added sources (ADR 0111).</summary>
public sealed record SongFold
{
    /// <summary>The ledger identity whose runs stay but no longer decide anything.</summary>
    public required string Song { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The upload's whole-source files already on disk, which become the added source's; none means a download.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>The files whose names do not fit the added source, and what they become.</summary>
    public IReadOnlyList<FileMove> Renaming { get; init; } = [];

    /// <summary>The label the file on disk already carries, which keeps its name if the added source takes it.</summary>
    public string? FileLabel => Files is [var first, ..] ? StemFileName.PresetOf(first) : null;
}

/// <summary>What adding a source actually did.</summary>
public sealed record AddOutcome
{
    public AddedSource? Source { get; init; }

    public SongFold? Fold { get; init; }

    /// <summary>What the upload offers, when that is why nothing was added.</summary>
    public ProbedSource? Shortfall { get; init; }

    public string? Problem { get; init; }

    public bool Succeeded => Problem is null && Shortfall is null;
}
