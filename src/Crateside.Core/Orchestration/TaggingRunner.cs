using Crateside.Core.Audit;
using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Applies the key the ledger settled, as Ableton tags, to one track's files in
/// <c>Sample Staging</c>.
/// </summary>
/// <remarks>
/// <para>
/// The last automated step, and the one that removes an error class rather than detecting it:
/// the user has mis-tagged a stem by hand and caught it by chance, which is what ADR 0003 was
/// written to fix.
/// </para>
/// <para>
/// <strong>Only files with no key applied are touched.</strong> A disagreement between an
/// applied tag and the ledger has two causes that look identical from here — the tag was wrong,
/// or a later verdict superseded it — and ADR 0003 leaves that judgement to the user. Filling
/// a blank is unambiguous; overwriting is not.
/// </para>
/// <para>
/// <strong><c>Sample Staging</c> only, never <c>Mashup Samples</c>.</strong> The finished
/// library holds hand-applied tags crateside knows nothing about.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// <para>
    /// The type comes from the stem's own <strong>provenance</strong> rather than its filename,
    /// because the user renames files as one of the human-limited steps and the comment survives
    /// that where a name does not.
    /// </para>
    /// <para>
    /// The mapping is read off the user's own 433 tagged samples rather than chosen:
    /// <c>Vocal - *</c> is <c>Type|Vocal</c>, <c>Instrumental - *</c> is
    /// <c>Type|Instrumental</c>, and a file with no preset at all — an instrumental release's
    /// source track, which is a sample and no kind of stem — is <c>Type|Instrumental</c>, which
    /// is how all twelve such files in the library are tagged. A <strong>warp guide</strong>
    /// gets its key and no type, matching every drums entry in staging; it is deleted rather
    /// than kept, so no drums entry exists in the finished library at all.
    /// </para>
    /// </remarks>
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
            // Drums, and anything else the separator grows later. Better to leave the type off
            // than to guess one into a vocabulary that keeps whatever it is given.
            _ => null,
        };

    /// <summary>
    /// Splits the ledger's spelling — <c>G# major</c> — into the two keywords Ableton keeps,
    /// <c>Key|G♯</c> and <c>Key|Major</c>.
    /// </summary>
    /// <remarks>
    /// The sharp is translated to <strong>U+266F</strong> here and validated again by the
    /// writer, because ADR 0003 finding 8 is unforgiving: an ASCII hash is not rejected, it
    /// silently creates a second permanent "F#" tag beside the real one.
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
/// <remarks>
/// <para>
/// <strong>The audit is the work list, rather than "whatever handoff just moved".</strong> That
/// is what makes this idempotent: a write crateside loses to a concurrent Ableton write shows up
/// as untagged on the next pass and is simply redone. It also reaches files that arrived some
/// other way, and it needs no coupling to the stage before it — which matters, because handoff
/// consumes its own trigger when it empties the folder it selects from.
/// </para>
/// <para>
/// Grouped by track, so one track's files become one write. Key is a song-level fact, so this
/// is also the natural unit: a track's stems are tagged together or not at all.
/// </para>
/// </remarks>
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
            .Run(ledger, [paths.SampleStaging])
            .Untagged.GroupBy(s => s.SourceVideoId, StringComparer.Ordinal)
            .FirstOrDefault(g => IsEligible(g.Key));

        if (next is null)
            return false;

        videoId = next.Key;
        stems = [.. next];

        return true;
    }

    /// <remarks>
    /// <c>retrySucceeded</c> is true: the audit already checked reality and said these files
    /// have no key, so a recorded success with untagged files means the write did not survive —
    /// and rewriting it is a few hundred bytes.
    /// </remarks>
    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Tagging, maxStrikes, retrySucceeded: true);
}
