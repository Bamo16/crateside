using Crateside.Core.Samples;
using Crateside.Core.Staging;
using Crateside.Core.Warps;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Says whether a re-cut's new stems can go straight to the library, which holds when nothing is
/// left for the user to warp (ADR 0103).
/// </summary>
public sealed class LibraryHandoff(CratesidePaths paths, WrittenWarps written)
{
    /// <summary>
    /// The library folder to hand the stems to, or null when they go to Staging for the user to warp.
    /// </summary>
    /// <param name="isOwnSource">The stems are cut from the track's own upload, not a source added to it.</param>
    public string? FolderFor(
        string trackKey,
        IReadOnlyDictionary<string, PreparedSidecar> prepared,
        bool isOwnSource
    )
    {
        if (
            !isOwnSource
            || prepared is { Count: 0 }
            || prepared.Values.Any(sidecar => sidecar is not { From: not null, IsUnwarped: false })
            || paths.LibraryFolder is not { Length: > 0 }
            || StagedSamples(trackKey).Any()
        )
            return null;

        var inLibrary = Samples(paths.LibraryFolder, trackKey).ToList();

        // Every stem the track already has is in the library and warped by the user, so the
        // new ones can take that warp as it stands.
        return inLibrary.Count > 0 && inLibrary.All(IsWarped)
            ? Path.GetDirectoryName(inLibrary[0])
            : null;
    }

    private IEnumerable<string> StagedSamples(string trackKey) =>
        Samples(paths.StagingFolder, trackKey);

    private static IEnumerable<string> Samples(string root, string trackKey) =>
        StemTagReader
            .FilesCutFrom(root, trackKey)
            .Where(path => !StagedFile.IsWarpGuidePreset(StemTagReader.PresetOf(path)));

    private bool IsWarped(string audioPath) =>
        AbleKit.Analysis.AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var file)
        && file.IsDefaultClipSaved
        && !written.IsCratesides(audioPath);
}
