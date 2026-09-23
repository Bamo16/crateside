namespace Crateside.Core.Staging;

/// <summary>
/// Finds the tag store file for a root folder, for the reader and the writer alike.
/// </summary>
/// <remarks>
/// A root can hold more than one. The reader and the writer have to agree on which, or crateside
/// reads back its own write from a file Ableton is not using and reports a tag the browser does
/// not have (#40).
/// </remarks>
internal static class AbletonFolderInfoFile
{
    internal const string Directory = "Ableton Folder Info";

    /// <summary>
    /// The store this root's tags live in, or null when it has none.
    /// </summary>
    /// <remarks>
    /// The most recently written, not the first Ableton happens to enumerate: enumeration order
    /// is unspecified, and the live file is the one being written. A crateside write makes its
    /// own file newest, so the choice stays put once made.
    /// </remarks>
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
