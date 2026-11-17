using Crateside.Core.Samples;

namespace Crateside.Core.Library;

/// <summary>A track's files in the library, by the upload they were cut from.</summary>
public interface ILibraryFiles
{
    IReadOnlyList<string> Of(string trackKey);

    /// <summary>Several tracks' files at once; an implementation that can read them in one pass does.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Of(IReadOnlyCollection<string> trackKeys) =>
        trackKeys.ToDictionary(key => key, Of);
}

/// <summary>The library read fresh on every call; a sourceless song is found by its key.</summary>
public sealed class LibraryFiles(CratesidePaths paths) : ILibraryFiles
{
    public IReadOnlyList<string> Of(string trackKey) =>
        SourcelessSongs.IsKey(trackKey)
            ? SourcelessSongs.FilesOf(paths.LibraryFolder, trackKey)
            : StemTagReader.FilesCutFrom(paths.LibraryFolder, trackKey);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Of(
        IReadOnlyCollection<string> trackKeys
    )
    {
        var cut = StemTagReader.FilesCutFrom(
            paths.LibraryFolder,
            trackKeys.Where(key => !SourcelessSongs.IsKey(key)).ToHashSet(StringComparer.Ordinal)
        );

        return trackKeys.ToDictionary(
            key => key,
            key =>
                SourcelessSongs.IsKey(key)
                    ? SourcelessSongs.FilesOf(paths.LibraryFolder, key)
                    : cut.GetValueOrDefault(key) ?? []
        );
    }
}
