using AbleKit.Tags;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;

namespace Crateside.Core.Ableton;

/// <summary>The keywords crateside will write: Live's own key and type, and the groups it settles itself.</summary>
public static class CratesideKeywords
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "Vocal",
        "Instrumental",
        "Loop",
    };

    public static bool IsKnown(string keyword) =>
        keyword.Split('|') switch
        {
            ["Key", var value] => KeyTags.Tonics.Contains(value) || KeyTags.Modes.Contains(value),
            ["Type", var value] => Types.Contains(value),
            [GenreVocabulary.Group, var value] => GenreVocabulary.IsGenre(value),
            [FamiliarityVocabulary.Group, var value] => FamiliarityVocabulary.IsValue(value),
            _ => false,
        };

    /// <summary>Writes nothing unless every keyword is known, so a misspelling never becomes a tag.</summary>
    public static TagWriteOutcome Apply(
        FolderInfoWriter writer,
        string rootFolder,
        IReadOnlyList<TagAssignment> assignments
    ) =>
        assignments.SelectMany(a => a.Keywords).FirstOrDefault(k => !IsKnown(k)) is { } bad
            ? new TagWriteOutcome.Rejected($"'{bad}' is not a keyword Ableton already knows")
            : writer.Apply(rootFolder, assignments);
}
