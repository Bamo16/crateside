using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Samples;

/// <summary>Appends what a file never cut is to the provenance it already carries (ADR 0028).</summary>
public static class SampleTyping
{
    /// <summary>Writes <paramref name="type"/> into the file's comment. True when it already says so.</summary>
    public static bool TryWrite(
        string path,
        SampleType type,
        [NotNullWhen(false)] out string? problem
    )
    {
        try
        {
            using var file = TagLib.File.Create(path);

            var provenance = StemProvenance.Parse(file.Tag.Comment);

            problem = provenance switch
            {
                null => "The file carries no provenance to add a type to.",
                { Preset: { } preset } =>
                    $"The file is a {preset} stem, so its preset says what it is.",
                { Type: { } written } when written != type =>
                    $"The file already says it is {written}.",
                _ => null,
            };

            if (problem is not null)
                return false;

            if (provenance is { Type: null })
            {
                file.Tag.Comment = $"{file.Tag.Comment} | {StemProvenance.TypePrefix}{type}";
                file.Save();
            }

            return true;
        }
        catch (Exception e)
        {
            problem = $"The file could not be written: {e.Message}";
            return false;
        }
    }
}
