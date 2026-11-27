using Crateside.Core.Orchestration;
using Crateside.Core.Plans;
using Crateside.Core.Tracks;

namespace Crateside.Core.Playback;

/// <summary>Where a track's full mix is, for the player's first picker row (ADR 0094).</summary>
public abstract record MixState
{
    private MixState() { }

    /// <summary>Kept as the track's Source sample, in Staging or the library, which also has a lane.</summary>
    public sealed record KeptAsSource(bool InLibrary) : MixState;

    /// <summary>On disk in the working set; <paramref name="Until"/> is null when retention is forever or its clock has not started.</summary>
    public sealed record Held(DateTimeOffset? Until, bool Forever) : MixState;

    /// <summary>Imported, and never pruned.</summary>
    public sealed record Imported : MixState;

    public sealed record Gone(bool CanFetch) : MixState;

    public sealed record Fetching : MixState;

    /// <summary>The plan keeps it, and no Source sample holds it yet.</summary>
    public sealed record Keeping : MixState;
}

public sealed class MixStatus(
    PlayableFiles files,
    WorkingSet workingSet,
    SourceRetentionStore retention,
    MixFetches fetches,
    CratesidePaths paths
)
{
    public MixState Of(TrackRow row)
    {
        var uploads = PlayableFiles.UploadsOf(row);

        if (uploads.Any(fetches.IsFetching))
            return new MixState.Fetching();

        if (
            row.Samples.FirstOrDefault(sample =>
                sample.Kind is StemKind.Source && !row.IsAddedSource(sample.SourceTrackKey)
            ) is
            { } kept
        )
            return new MixState.KeptAsSource(kept.IsInLibrary);

        if (files.MixPathOf(row) is not { } path)
            return row.Plan.KeepSource && row.HasUpload
                ? new MixState.Keeping()
                : new MixState.Gone(row.HasUpload && TrackKeys.IsVideoId(uploads[0]));

        if (row.Plan.KeepSource)
            return new MixState.Keeping();

        return path.StartsWith(paths.ImportsFolder, StringComparison.OrdinalIgnoreCase)
            ? new MixState.Imported()
            : HeldBy(uploads);
    }

    private MixState.Held HeldBy(IReadOnlyList<string> uploads)
    {
        var keeping = retention.Read();
        var trackKey = uploads.FirstOrDefault(key => workingSet.TryGetSource(key, out _));

        return trackKey is not null && workingSet.LeftStagingAt(trackKey) is { } left
            ? new MixState.Held(
                keeping.LapsesAt(left, workingSet.MinimumRetention(trackKey)),
                keeping.Forever
            )
            : new MixState.Held(null, keeping.Forever);
    }
}
