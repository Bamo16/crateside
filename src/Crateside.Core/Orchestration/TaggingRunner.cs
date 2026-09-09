using Crateside.Core.Audit;
using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Applies the key the ledger settled, as Ableton tags, to one track's files in
/// <c>Sample Staging</c>. Only files with no key applied are touched.
/// </summary>
public sealed class TaggingRunner(CratesidePaths paths, AbletonFolderInfoWriter? writer = null)
{
    private readonly AbletonFolderInfoWriter _writer = writer ?? new AbletonFolderInfoWriter();

    /// <summary>Tags every untagged file belonging to one track.</summary>
    public TagWriteOutcome Run(IReadOnlyList<TaggedStem> stems)
    {
        List<TagAssignment> assignments =
        [
            .. stems
                .Select(s => new { Stem = s, Keywords = KeywordsFor(s) })
                .Where(x => x.Keywords.Count > 0)
                .Select(x => new TagAssignment
                {
                    RelativePath = Path.GetRelativePath(paths.SampleStaging, x.Stem.Path),
                    Keywords = x.Keywords,
                }),
        ];

        return assignments.Count is 0
            ? TagWriteOutcome.Rejected("nothing about these files could be turned into tags")
            : _writer.Apply(paths.SampleStaging, assignments);
    }

    /// <summary>
    /// The keywords one file should carry: the key from the ledger, and the type from what the
    /// separator made it.
    /// </summary>
    internal static IReadOnlyList<string> KeywordsFor(TaggedStem stem)
    {
        if (
            stem.LedgerKey is not { Length: > 0 } key
            || !TryReadKey(key, out var tonic, out var mode)
        )
            return [];

        List<string> keywords = [$"Key|{tonic}", $"Key|{mode}"];

        if (TypeFor(stem.Preset) is { } type)
            keywords.Add($"Type|{type}");

        return keywords;
    }

    private static string? TypeFor(string? preset) =>
        preset switch
        {
            null or "" => "Instrumental",
            var p when p.StartsWith("Vocal", StringComparison.OrdinalIgnoreCase) => "Vocal",
            var p when p.StartsWith("Instrumental", StringComparison.OrdinalIgnoreCase) =>
                "Instrumental",
            _ => null,
        };

    /// <summary>
    /// Splits the ledger's spelling — <c>G# major</c> — into the two keywords Ableton keeps,
    /// <c>Key|G♯</c> and <c>Key|Major</c>.
    /// </summary>
    /// <remarks>
    /// An ASCII hash is not rejected by Ableton — it silently creates a second, permanent
    /// "F#" tag beside the real one.
    /// </remarks>
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

        return AbletonFolderInfoWriter.Tonics.Contains(tonic) && mode is "Major" or "Minor";
    }
}

/// <summary>
/// Decides whose tags to write next, by asking the audit what is untagged.
/// </summary>
public sealed class TaggingSelector(
    JobStore jobs,
    CratesidePaths paths,
    int maxStrikes = TaggingSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 3;

    /// <summary>The next track with files in staging that have a verdict and no key applied.</summary>
    public bool TryPickNext(
        KeyLedger ledger,
        out string videoId,
        out IReadOnlyList<TaggedStem> stems
    )
    {
        videoId = null!;
        stems = [];

        var next = TagAudit
            .Run(ledger, [AuditRoot.Written(paths.SampleStaging)])
            .Untagged.GroupBy(s => s.SourceVideoId, StringComparer.Ordinal)
            .FirstOrDefault(g => IsEligible(g.Key));

        if (next is null)
            return false;

        videoId = next.Key;
        stems = [.. next];

        return true;
    }

    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Tagging, maxStrikes, retrySucceeded: true);
}
