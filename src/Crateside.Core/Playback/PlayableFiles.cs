using Crateside.Core.Orchestration;
using Crateside.Core.Plans;
using Crateside.Core.Tracks;

namespace Crateside.Core.Playback;

/// <summary>What the player can play of a track: the mix, then each stem on disk (ADR 0094).</summary>
public sealed class PlayableFiles(SourceIndex sources, CratesidePaths paths)
{
    public const string MixId = "mix";

    /// <summary>The mix, if it is on disk, then every stem that is, each under the id the audio endpoint takes.</summary>
    public IReadOnlyList<PlayableFile> Of(TrackRow row)
    {
        var stems = row
            .Samples.Where(sample => sample.Kind is not StemKind.Source)
            .Select(sample =>
                Resolve(sample.Path) is { } path
                    ? new PlayableFile(Path.GetFileName(sample.Path), sample.Label, path)
                    : null
            )
            .OfType<PlayableFile>();

        return MixPathOf(row) is { } mix
            ? [new PlayableFile(MixId, "Mix", mix), .. stems]
            : [.. stems];
    }

    public static bool Knows(TrackRow row, string id) =>
        string.Equals(id, MixId, StringComparison.Ordinal)
        || row.Samples.Any(sample =>
            sample.Kind is not StemKind.Source
            && string.Equals(Path.GetFileName(sample.Path), id, StringComparison.OrdinalIgnoreCase)
        );

    public PlayableFile? Find(TrackRow row, string id) =>
        string.Equals(id, MixId, StringComparison.Ordinal)
            ? MixPathOf(row) is { } mix
                ? new PlayableFile(MixId, "Mix", mix)
                : null
            : row.Samples.FirstOrDefault(sample =>
                sample.Kind is not StemKind.Source
                && string.Equals(
                    Path.GetFileName(sample.Path),
                    id,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                is { } stem
            && Resolve(stem.Path) is { } path
                ? new PlayableFile(id, stem.Label, path)
                : null;

    /// <summary>The upload each of the track's files may have been cut from, the one in use first.</summary>
    public static IReadOnlyList<string> UploadsOf(TrackRow row) =>
        [
            .. row
                .Uploads.Select(upload => upload.TrackKey)
                .Append(row.TrackKey)
                .Distinct(StringComparer.Ordinal),
        ];

    public string? MixPathOf(TrackRow row) =>
        UploadsOf(row).Select(sources.PathOf).FirstOrDefault(path => path is not null)
        ?? row.Samples.Where(sample => sample is { Kind: StemKind.Source })
            .Select(sample => Resolve(sample.Path))
            .FirstOrDefault(path => path is not null);

    /// <summary>The file where it is now: Live moves a stem from Staging to the library, and the row may not have heard yet.</summary>
    private string? Resolve(string path) =>
        File.Exists(path)
            ? path
            : new[] { paths.StagingFolder, paths.LibraryFolder }
                .Select(folder => Path.Combine(folder, Path.GetFileName(path)))
                .FirstOrDefault(File.Exists);
}

/// <param name="Id">What the audio endpoint takes: <c>mix</c>, or the stem's file name.</param>
/// <param name="Name">What the picker calls it.</param>
public sealed record PlayableFile(string Id, string Name, string Path);
