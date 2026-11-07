using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Crateside.Core.Samples;

/// <summary>
/// Takes a file crateside did not make into a track, by writing the provenance every reader
/// joins on (ADR 0022).
/// </summary>
public static class StemAdoption
{
    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>The comment written into an adopted file, and read back by <see cref="StemProvenance"/>.</summary>
    public static string Comment(string trackKey, string label, SampleType type, DateOnly on) =>
        string.Join(
            " | ",
            $"{StemProvenance.AdoptedPrefix}{Version}",
            $"label: {label}",
            $"date: {on:yyyy-MM-dd}",
            $"source: https://www.youtube.com/watch?v={trackKey}",
            $"{StemProvenance.TypePrefix}{type}"
        );

    /// <summary>
    /// Writes the provenance into <paramref name="path"/>, so the file reads as a sample of
    /// <paramref name="trackKey"/> — refusing a file that already carries some.
    /// </summary>
    public static bool TryAdopt(
        string path,
        string trackKey,
        string label,
        SampleType type,
        TimeProvider time,
        [NotNullWhen(false)] out string? problem
    )
    {
        if (!File.Exists(path))
        {
            problem = "There is no file there.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            problem = "An adopted sample needs a label, which is how the plan names it.";
            return false;
        }

        try
        {
            using var file = TagLib.File.Create(path);

            if (StemProvenance.Parse(file.Tag.Comment) is { } existing)
            {
                problem = existing.IsAdopted
                    ? "That file has already been adopted."
                    : "That file carries separation provenance, so it belongs to a track already.";

                return false;
            }

            file.Tag.Comment = Comment(
                trackKey,
                label,
                type,
                DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
            );
            file.Save();
        }
        catch (Exception ex)
        {
            problem = $"The file could not be written: {ex.Message}";
            return false;
        }

        problem = null;
        return true;
    }
}
