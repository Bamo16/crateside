using System.Text.RegularExpressions;
using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Audit;

/// <summary>
/// Reads every stem's applied Ableton tags and hands them to <see cref="TagAuditReport"/>,
/// which does the interpreting.
/// </summary>
/// <remarks>
/// <para>
/// The staging board already flags a stem whose Ableton key disagrees with the verdict, but
/// only while the stem is still <em>in</em> staging. One that was mis-tagged and then moved to
/// <c>Mashup Samples</c> leaves the board and is never looked at again — which is the case that
/// actually bites, because the applied tag is what pairing uses, so a wrong one silently
/// misroutes mashups for as long as it lives.
/// </para>
/// <para>
/// Read-only, and safe while Ableton is running.
/// </para>
/// </remarks>
public static class TagAudit
{
    public static TagAuditReport Run(
        KeyLedger ledger,
        IEnumerable<string> stemRoots,
        AbletonFolderInfoReader? folderInfoReader = null
    ) => new(Scan(ledger, stemRoots, folderInfoReader));

    private static List<TaggedStem> Scan(
        KeyLedger ledger,
        IEnumerable<string> stemRoots,
        AbletonFolderInfoReader? folderInfoReader
    )
    {
        var folders = folderInfoReader ?? new AbletonFolderInfoReader();
        var stems = new List<TaggedStem>();

        foreach (var root in stemRoots.Where(Directory.Exists))
        {
            var folderInfo = folders.Read(root);

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!StagingBoard.IsStemFile(path))
                    continue;

                // No provenance means no video ID, so there is nothing to group or join on.
                // Common for anything not produced by StemForge, and not a finding.
                if (StemTagReader.Read(path)?.Provenance?.SourceVideoId is not { } videoId)
                    continue;

                var hasVerdict = ledger.TryGetLatestByVideoId(videoId, out var run);
                folderInfo.TryGet(path, out var applied);

                stems.Add(
                    new TaggedStem
                    {
                        Path = path,
                        FileName = Path.GetFileName(path),
                        Root = root,
                        SourceVideoId = videoId,
                        Song = run?.Song,
                        LedgerKey = run?.Key,
                        HasTaggableVerdict = hasVerdict && run!.HasTaggableKey,
                        Confidence = run?.Confidence ?? KeyConfidence.Unknown,
                        AppliedKey = applied?.Key,
                        HasPartialKey = applied?.HasPartialKey ?? false,
                    }
                );
            }
        }

        return stems;
    }
}

/// <summary>
/// What the audit found. Every property is a different reading of the same stem list, so the
/// list is the constructor's whole input and none of the readings are settable.
/// </summary>
/// <remarks>
/// <para>
/// Two independent checks, because they cover different stems:
/// </para>
/// <list type="bullet">
/// <item><strong>Against the ledger</strong> — the applied key versus the current verdict.
/// Only possible for stems the ledger knows.</item>
/// <item><strong>Against each other</strong> — two stems of one song must carry the same key,
/// since key is a song-level fact. This needs no verdict at all, so it reaches the large share
/// of the library that predates the ledger.</item>
/// </list>
/// <para>
/// <strong>A ledger disagreement does not say who is wrong.</strong> It has two causes that
/// look identical from here: the tag was applied incorrectly, or the ledger superseded it with
/// a better verdict afterwards. Nothing records when a tag was written, so crateside reports
/// both sides and leaves the judgement to the user. A <em>sibling</em> disagreement is
/// different — it is wrong on its own terms whatever the ledger says.
/// </para>
/// <para>
/// <c>stems</c> is read by the initializers below and then goes away: a primary constructor
/// parameter used only in initializers is not captured into a field, so the full list is not
/// retained or exposed.
/// </para>
/// </remarks>
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

    /// <summary>
    /// A taggable verdict exists but no key was applied — the step was skipped. Deliberately
    /// <em>not</em> counted as a problem while tagging is still the user's job: it is ordinary
    /// work in progress, and a badge that lit up mid-batch would train them to ignore it.
    /// <strong>Once crateside owns tagging (ADR 0003), an untagged stem stops being ordinary
    /// and should move into <see cref="ProblemCount"/>.</strong>
    /// </summary>
    public IReadOnlyList<TaggedStem> Untagged { get; } =
    [.. stems.Where(s => s is { AppliedKey: null, HasTaggableVerdict: true })];

    /// <summary>Tagged with a tonic but no mode, or the reverse. Half a key is not a key.</summary>
    public IReadOnlyList<TaggedStem> PartialKeys { get; } = [.. stems.Where(s => s.HasPartialKey)];

    /// <summary>
    /// Songs whose own stems carry different keys — grouped by source video ID, which survives
    /// the rename step where filenames do not. Needs no verdict, so this is the only check that
    /// reaches stems the ledger has never seen.
    /// </summary>
    public IReadOnlyList<InconsistentSong> Inconsistencies { get; } = FindInconsistencies(stems);

    /// <summary>Stems whose applied tag matches the current verdict exactly.</summary>
    public int AgreedCount { get; } =
        stems.Count(s =>
            s is { AppliedKey: { } applied, LedgerKey: { } verdict }
            && applied.Equals(verdict, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Stems with provenance the ledger has never seen.</summary>
    public int NoVerdictCount { get; } = stems.Count(s => s.Song is null);

    /// <summary>Every stem carrying usable provenance, whether or not it produced a finding.</summary>
    public int ScannedCount { get; } = stems.Count;

    /// <summary>Findings worth showing a badge for. See <see cref="Untagged"/> for what is not.</summary>
    public int ProblemCount => Disagreements.Count + PartialKeys.Count + Inconsistencies.Count;

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

    /// <summary>Which stem root it was found under.</summary>
    public required string Root { get; init; }

    public required string SourceVideoId { get; init; }

    /// <summary>The ledger identity, or null when the ledger has never seen this stem.</summary>
    public string? Song { get; init; }

    /// <summary>The current verdict's key. Null when withheld, or when there is no verdict.</summary>
    public string? LedgerKey { get; init; }

    /// <summary>Whether the verdict produced a key the user would actually tag with.</summary>
    public bool HasTaggableVerdict { get; init; }

    public KeyConfidence Confidence { get; init; }

    /// <summary>The key Ableton actually has on the file, or null when untagged.</summary>
    public string? AppliedKey { get; init; }

    public bool HasPartialKey { get; init; }
}

/// <summary>A song whose stems do not agree with each other.</summary>
/// <remarks>
/// Wrong on its own terms: key is a song-level fact, so two stems of one song carrying
/// different keys cannot both be right, whatever the ledger does or does not say.
/// </remarks>
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
