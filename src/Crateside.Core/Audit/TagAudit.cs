using System.Text.RegularExpressions;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Samples;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Audit;

/// <summary>Reads the tag state an audit of <paramref name="root"/> should be based on.</summary>
public delegate AbletonFolderInfo ReadTagState(AuditRoot root);

/// <summary>
/// Reads every stem's applied Ableton tags and hands them to <see cref="TagAuditReport"/>,
/// which does the interpreting.
/// </summary>
public static class TagAudit
{
    /// <param name="tagState">
    /// Where each root's tags are read from. The default is the rule, not a convenience — see
    /// <see cref="AuditRoot.ReadsTheIndex"/>.
    /// </param>
    public static TagAuditReport Run(
        KeyLedger ledger,
        IEnumerable<AuditRoot> roots,
        ReadTagState? tagState = null
    ) => new(Scan(ledger, roots, tagState ?? FromEitherStore));

    /// <summary>
    /// Reads the store this root's tags live in — its XMP where whole, else Ableton's index at the
    /// default location; <see cref="Using"/> takes configured paths (ADR 0017).
    /// </summary>
    public static AbletonFolderInfo FromEitherStore(AuditRoot root) =>
        Using(new AbletonIndexReader(Options.Create(new CratesidePaths())))(root);

    /// <summary>The same rule, against readers the caller already has.</summary>
    public static ReadTagState Using(
        AbletonIndexReader index,
        AbletonFolderInfoReader? folderInfo = null
    ) =>
        root =>
            root.ReadsTheIndex
                ? index.Read(root.Path)
                : (folderInfo ?? new AbletonFolderInfoReader()).Read(root.Path);

    private static List<TaggedStem> Scan(
        KeyLedger ledger,
        IEnumerable<AuditRoot> roots,
        ReadTagState tagState
    )
    {
        var stems = new List<TaggedStem>();

        foreach (var root in roots.Where(root => Directory.Exists(root.Path)))
        {
            var folderInfo = tagState(root);

            foreach (
                var path in Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories)
            )
            {
                if (!SampleFiles.IsAudio(path))
                    continue;

                if (
                    StemTagReader.Read(path)?.Provenance
                    is not { SourceVideoId: { } videoId } provenance
                )
                    continue;

                var hasVerdict = ledger.TryGetLatestByVideoId(videoId, out var run);
                folderInfo.TryGet(path, out var applied);

                stems.Add(
                    new TaggedStem
                    {
                        Path = path,
                        FileName = Path.GetFileName(path),
                        Root = root.Path,
                        SourceVideoId = videoId,
                        Song = run?.Song,
                        LedgerKey = run?.Key,
                        HasTaggableVerdict = hasVerdict && run!.HasTaggableKey,
                        Confidence = run?.Confidence ?? KeyConfidence.Unknown,
                        AppliedKey = applied?.Key,
                        HasPartialKey = applied?.HasPartialKey ?? false,
                        Preset = provenance.Preset,
                        Type = provenance.Type,
                    }
                );
            }
        }

        return stems;
    }
}

/// <summary>
/// A root to scan, and whether its tag store holds the root's whole tag state — which decides the
/// store read, not whether crateside writes it (ADR 0017, 0023).
/// </summary>
public sealed record AuditRoot(string Path, bool TagStoreIsWhole)
{
    /// <summary>
    /// Whether this root's tags come from Ableton's index, which lags hours behind, rather than
    /// its own XMP.
    /// </summary>
    public bool ReadsTheIndex => !TagStoreIsWhole;

    /// <summary>A root crateside tags into, read from the XMP its own writes land in.</summary>
    public static AuditRoot Written(string path) => new(path, true);

    /// <summary>A root whose store holds only some of its tags, read from Ableton's index.</summary>
    public static AuditRoot Mirrored(string path) => new(path, false);
}

/// <summary>What the audit found, as different readings of the same stem list.</summary>
public sealed partial class TagAuditReport(IReadOnlyList<TaggedStem> stems)
{
    /// <summary>
    /// Applied key and current verdict both present and different. Either the tag is wrong or
    /// the verdict superseded it; the audit does not claim to know which.
    /// </summary>
    public IReadOnlyList<TaggedStem> Disagreements { get; } =
    [
        .. stems.Where(s =>
            s is { AppliedKey: { } applied, LedgerKey: { } verdict }
            && !applied.Equals(verdict, StringComparison.OrdinalIgnoreCase)
        ),
    ];

    /// <summary>A taggable verdict exists but no key was applied.</summary>
    public IReadOnlyList<TaggedStem> Untagged { get; } =
    [.. stems.Where(s => s is { AppliedKey: null, HasTaggableVerdict: true })];

    /// <summary>Tagged with a tonic but no mode, or the reverse.</summary>
    public IReadOnlyList<TaggedStem> PartialKeys { get; } = [.. stems.Where(s => s.HasPartialKey)];

    /// <summary>
    /// Songs whose own stems carry different keys, grouped by source video ID since filenames
    /// do not survive the rename step.
    /// </summary>
    public IReadOnlyList<InconsistentSong> Inconsistencies { get; } = FindInconsistencies(stems);

    public int AgreedCount { get; } =
        stems.Count(s =>
            s is { AppliedKey: { } applied, LedgerKey: { } verdict }
            && applied.Equals(verdict, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Stems with provenance the ledger has never seen.</summary>
    public int NoVerdictCount { get; } = stems.Count(s => s.Song is null);

    /// <summary>Every stem carrying usable provenance, whether or not it produced a finding.</summary>
    public int ScannedCount { get; } = stems.Count;

    /// <summary>Findings worth showing a badge for.</summary>
    public int ProblemCount =>
        Disagreements.Count + PartialKeys.Count + Inconsistencies.Count + Untagged.Count;

    public bool IsClean => ProblemCount == 0;

    private static IReadOnlyList<InconsistentSong> FindInconsistencies(
        IReadOnlyList<TaggedStem> stems
    ) =>
        [
            .. stems
                .Where(s => s.AppliedKey is not null)
                .GroupBy(s => s.SourceVideoId, StringComparer.Ordinal)
                .Where(g =>
                    g.Select(s => s.AppliedKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    > 1
                )
                .Select(g => new InconsistentSong
                {
                    SourceVideoId = g.Key,
                    Label =
                        g.Select(s => s.Song).FirstOrDefault(s => s is not null)
                        ?? StripPresetSuffix(g.First().FileName),
                    LedgerKey = g.Select(s => s.LedgerKey).FirstOrDefault(k => k is not null),
                    Stems = [.. g],
                }),
        ];

    /// <summary>Drops StemForge's trailing <c>(Vocal - Full)</c> so siblings read as one song.</summary>
    private static string StripPresetSuffix(string fileName) =>
        PresetSuffix.Replace(Path.GetFileNameWithoutExtension(fileName), string.Empty).Trim();

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex PresetSuffix { get; }
}

/// <summary>One stem, as the audit sees it: what the ledger says, and what Ableton has.</summary>
public sealed record TaggedStem
{
    public required string Path { get; init; }
    public required string FileName { get; init; }

    public required string Root { get; init; }

    public required string SourceVideoId { get; init; }

    /// <summary>The ledger identity, or null when the ledger has never seen this stem.</summary>
    public string? Song { get; init; }

    /// <summary>The current verdict's key. Null when withheld, or when there is no verdict.</summary>
    public string? LedgerKey { get; init; }

    /// <summary>Whether the verdict produced a key the user would actually tag with.</summary>
    public bool HasTaggableVerdict { get; init; }

    public KeyConfidence Confidence { get; init; }

    /// <summary>The key Ableton has on the file, read from whichever store holds it.</summary>
    public string? AppliedKey { get; init; }

    public bool HasPartialKey { get; init; }

    /// <summary>
    /// The separation preset from the file's own provenance, which survives a rename, or null for
    /// a file that was never separated.
    /// </summary>
    public string? Preset { get; init; }

    public SampleType? Type { get; init; }
}

public sealed record InconsistentSong
{
    public required string SourceVideoId { get; init; }

    /// <summary>The ledger identity where there is one, else the filename minus its preset.</summary>
    public required string Label { get; init; }

    /// <summary>The verdict, when the ledger knows this song — often it will not.</summary>
    public string? LedgerKey { get; init; }

    public required IReadOnlyList<TaggedStem> Stems { get; init; }

    /// <summary>The distinct keys in play, which is the whole finding.</summary>
    public IReadOnlyList<string> AppliedKeys =>
        [
            .. Stems
                .Select(s => s.AppliedKey)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
}
