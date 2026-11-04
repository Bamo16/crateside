using Crateside.Core.Samples;

namespace Crateside.Core.Library;

/// <summary>A track's files in the library, by the upload they were cut from.</summary>
public interface ILibraryFiles
{
    IReadOnlyList<string> Of(string trackKey);
}

/// <summary>The library read fresh on every call; a sourceless song is found by its key.</summary>
public sealed class LibraryFiles(CratesidePaths paths) : ILibraryFiles
{
    public IReadOnlyList<string> Of(string trackKey) =>
        SourcelessSongs.IsKey(trackKey)
            ? SourcelessSongs.FilesOf(paths.LibraryFolder, trackKey)
            : StemTagReader.FilesCutFrom(paths.LibraryFolder, trackKey);
}
