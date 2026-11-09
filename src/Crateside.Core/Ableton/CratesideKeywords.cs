using System.Text.RegularExpressions;
using AbleKit.Tags;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;

namespace Crateside.Core.Ableton;

/// <summary>The keywords crateside will write: Live's own key and type, and the groups it settles itself.</summary>
public static partial class CratesideKeywords
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "Vocal",
        "Instrumental",
        "Loop",
    };

    [GeneratedRegex(@"^[1-9][0-9]*/[1-9][0-9]*$")]
    private static partial Regex Meter { get; }

    public static bool IsKnown(string keyword) =>
        keyword.Split('|') switch
        {
            ["Key", var value] => KeyTags.Tonics.Contains(value)
                || KeyTags.Modes.Contains(value)
                || ModeTags.IsModal(value),
            ["Type", var value] => Types.Contains(value),
            // One of Ableton's 29, a genre of the person's own, or one of Live's own content (ADR 0088).
            [GenreVocabulary.Group, var value] => GenreVocabulary.IsName(value),
            [FamiliarityVocabulary.Group, var value] => FamiliarityVocabulary.IsValue(value),
            // Set by the maintainer in Live (#154): any n/d, Mixed, or Odd bars.
            ["Meter", var value] => value is "Mixed" or "Odd bars" || Meter.IsMatch(value),
            _ => false,
        };

    /// <summary>Writes nothing unless every keyword is known, so a misspelling never becomes a tag.</summary>
    public static TagWriteOutcome Apply(
        FolderInfoWriter writer,
        string rootFolder,
        IReadOnlyList<TagAssignment> assignments
    ) =>
        assignments.SelectMany(a => a.Keywords ?? []).FirstOrDefault(k => !IsKnown(k)) is { } bad
            ? new TagWriteOutcome.Rejected($"'{bad}' is not a keyword Ableton already knows")
            : TagWriteOutcome.Of(() => writer.Apply(rootFolder, assignments));
}
