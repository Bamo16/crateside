using System.Reflection;

namespace Crateside.Core.Imports;

/// <summary>
/// Writes the provenance a kept copy of a non-YouTube track's source needs to join its track,
/// on the copy and never on the import (ADR 0077).
/// </summary>
public static class KeptSourceStamp
{
    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    public static string Comment(string trackKey, DateOnly on) =>
        string.Join(
            " | ",
            $"{Samples.StemProvenance.AdoptedPrefix}{Version}",
            $"date: {on:yyyy-MM-dd}",
            $"source: {TrackKeys.ProvenanceOf(trackKey)}"
        );

    /// <summary>False when the copy could not be written, with why.</summary>
    public static bool TryWrite(string copyPath, string trackKey, DateOnly on, out string? problem)
    {
        try
        {
            using var file = TagLib.File.Create(copyPath);

            file.Tag.Comment = Comment(trackKey, on);
            file.Save();
            problem = null;

            return true;
        }
        catch (Exception ex)
            when (ex
                    is TagLib.CorruptFileException
                        or TagLib.UnsupportedFormatException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            problem = ex.Message;

            return false;
        }
    }
}
