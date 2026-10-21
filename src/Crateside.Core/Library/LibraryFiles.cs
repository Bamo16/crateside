using Crateside.Core.Samples;

namespace Crateside.Core.Library;

/// <summary>A track's files in the library, by the upload they were cut from.</summary>
public interface ILibraryFiles
{
    IReadOnlyList<string> Of(string videoId);
}

/// <summary>The library read fresh on every call; a sourceless song is found by its key.</summary>
public sealed class LibraryFiles(CratesidePaths paths) : ILibraryFiles
{
    public IReadOnlyList<string> Of(string videoId) =>
        SourcelessSongs.IsKey(videoId)
            ? SourcelessSongs.FilesOf(paths.LibraryFolder, videoId)
            : StemTagReader.FilesCutFrom(paths.LibraryFolder, videoId);
}
