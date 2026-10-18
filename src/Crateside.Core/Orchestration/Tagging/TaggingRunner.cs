using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Audit;
using Crateside.Core.Ledger;
using Crateside.Core.Samples;

namespace Crateside.Core.Orchestration.Tagging;

/// <summary>
/// Applies the key the ledger settled, as Ableton tags, to one track's files in
/// <c>Staging</c>. Only files with no key applied are touched.
/// </summary>
public sealed class TaggingRunner(CratesidePaths paths, FolderInfoWriter? writer = null)
{
    private readonly FolderInfoWriter _writer = writer ?? new FolderInfoWriter();

    /// <summary>Tags every untagged file belonging to one track.</summary>
    public TagWriteOutcome Run(IReadOnlyList<TaggedStem> stems)
    {
        List<TagAssignment> assignments =
        [
            .. stems
                .Select(s => new
                {
                    Stem = s,
                    Keywords = KeywordsFor(s.LedgerKey, s.Preset, s.Type),
                })
                .Where(x => x.Keywords.Count > 0)
                .Select(x => new TagAssignment
                {
                    RelativePath = Path.GetRelativePath(paths.StagingFolder, x.Stem.Path),
                    Keywords = x.Keywords,
                }),
        ];

        return assignments.Count is 0
            ? new TagWriteOutcome.Rejected("nothing about these files could be turned into tags")
            : CratesideKeywords.Apply(_writer, paths.StagingFolder, assignments);
    }

    /// <summary>
    /// The keywords one file should carry: the key from the ledger, and the type from its preset or,
    /// for a file never cut, from what the user said it is.
    /// </summary>
    internal static IReadOnlyList<string> KeywordsFor(
        string? ledgerKey,
        string? preset,
        SampleType? type
    )
    {
        if (ledgerKey is not { Length: > 0 } key || !TryReadKey(key, out var tonic, out var mode))
            return [];

        List<string> keywords = [$"Key|{tonic}", $"Key|{mode}"];

        if (TypeFor(preset, type) is { } tagged)
            keywords.Add($"Type|{tagged}");

        return keywords;
    }

    /// <summary>A file with neither is a kept source, used as an instrumental (ADR 0028).</summary>
    internal static string? TypeFor(string? preset, SampleType? type) =>
        preset switch
        {
            null or "" => (type ?? SampleType.Instrumental).ToString(),
            var p when p.StartsWith("Vocal", StringComparison.OrdinalIgnoreCase) => "Vocal",
            var p when p.StartsWith("Instrumental", StringComparison.OrdinalIgnoreCase) =>
                "Instrumental",
            _ => null,
        };

    /// <summary>
    /// Splits the ledger's spelling — <c>G# major</c> — into the two keywords Ableton keeps,
    /// <c>Key|G♯</c> and <c>Key|Major</c> — never an ASCII hash, which makes a second tag.
    /// </summary>
    private static bool TryReadKey(string key, out string tonic, out string mode)
    {
        tonic = string.Empty;
        mode = string.Empty;

        if (
            key.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [var rawTonic, var rawMode]
        )
            return false;

        tonic = rawTonic.Replace('#', '♯');
        mode = char.ToUpperInvariant(rawMode[0]) + rawMode[1..].ToLowerInvariant();

        return KeyTags.Tonics.Contains(tonic) && mode is "Major" or "Minor";
    }
}
