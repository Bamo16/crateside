using System.Diagnostics.CodeAnalysis;
using AbleKit.Analysis;
using AbleKit.Tags;

namespace Crateside.Core.Ableton;

/// <summary>
/// Reads of Live's files for views and scans, where a file Live is writing, or one that cannot be
/// read, counts as absent until the next read.
/// </summary>
public static class AbletonReads
{
    /// <param name="read">A <see cref="FolderInfoReader"/> or <see cref="FileIndexReader"/>'s <c>Read</c>.</param>
    public static FolderInfo TagsOrEmpty(Func<string, FolderInfo> read, string folder)
    {
        try
        {
            return read(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FolderInfo.Empty(folder);
        }
    }

    /// <summary>False when the sidecar is missing, locked, or not one AbleKit reads.</summary>
    public static bool TryReadAnalysis(string path, [NotNullWhen(true)] out AnalysisFile? analysis)
    {
        try
        {
            return AnalysisFile.TryRead(path, out analysis);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            analysis = null;

            return false;
        }
    }
}
