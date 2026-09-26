namespace Crateside.Core.Ableton;

/// <summary>
/// Finds the tag store file for a root folder, so the reader and the writer agree on which of
/// several it is (#40).
/// </summary>
internal static class AbletonFolderInfoFile
{
    internal const string Directory = "Ableton Folder Info";

    /// <summary>
    /// The store this root's tags live in — the most recently written, since that is the live one
    /// — or null when it has none.
    /// </summary>
    internal static string? In(string rootFolder) =>
        All(rootFolder).MaxBy(File.GetLastWriteTimeUtc);

    /// <summary>Every store file under this root, newest first.</summary>
    internal static IReadOnlyList<string> All(string rootFolder)
    {
        var directory = Path.Combine(rootFolder, Directory);

        return System.IO.Directory.Exists(directory)
            ?
            [
                .. System
                    .IO.Directory.EnumerateFiles(directory, "*.xmp")
                    .OrderByDescending(File.GetLastWriteTimeUtc),
            ]
            : [];
    }
}
