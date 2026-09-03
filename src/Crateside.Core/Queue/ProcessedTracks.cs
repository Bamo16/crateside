using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Core.Queue;

/// <summary>
/// Which source video IDs are <strong>fully processed</strong>: a ledger verdict
/// <em>and</em> a stem carrying that video ID on disk.
/// </summary>
/// <remarks>
/// <para>
/// Both halves are required, and that is the whole point — it is what makes the test immune
/// to partial failure. A run that recorded a key and then died during separation has a
/// verdict but no stem, fails the test, and stays queued. The predicate is idempotent, so
/// re-running it is always safe.
/// </para>
/// <para>
/// A WITHHELD verdict still counts as processed: the work was done, and the answer was
/// "no single safe tag". Re-queueing it would only produce the same answer.
/// </para>
/// </remarks>
public sealed class ProcessedTracks
{
    private ProcessedTracks(
        Dictionary<string, List<StemOnDisk>> stemsByVideoId,
        IReadOnlySet<string> fullyProcessed,
        IReadOnlySet<string> verdictWithoutStems
    )
    {
        StemsByVideoId = stemsByVideoId.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<StemOnDisk>)entry.Value,
            StringComparer.Ordinal
        );
        FullyProcessed = fullyProcessed;
        VerdictWithoutStems = verdictWithoutStems;
    }

    /// <summary>
    /// Every file the scan found, by the video ID it came from.
    /// </summary>
    /// <remarks>
    /// The whole map, for a caller that does not already know which tracks to ask about. The
    /// Tracks page is one: a track leaves the playlist as soon as it is processed while its files
    /// sit in staging for weeks afterwards, so asking once per playlist item finds nothing for
    /// exactly the rows that view exists to show.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>> StemsByVideoId { get; }

    /// <summary>Video IDs that satisfy both halves of the test.</summary>
    public IReadOnlySet<string> FullyProcessed { get; }

    /// <summary>
    /// Video IDs with a verdict but no stem anywhere on disk — a separation that never
    /// finished, or stems the user deleted. These deliberately stay queued.
    /// </summary>
    public IReadOnlySet<string> VerdictWithoutStems { get; }

    /// <summary>Where the stems for a video ID live. Empty when none are on disk.</summary>
    public IReadOnlyList<StemOnDisk> StemsFor(string videoId) =>
        StemsByVideoId.GetValueOrDefault(videoId) ?? [];

    public bool IsFullyProcessed(string videoId) => FullyProcessed.Contains(videoId);

    /// <summary>
    /// When a file arrived, or null where the filesystem will not say.
    /// </summary>
    /// <remarks>
    /// Null rather than a sentinel: Windows returns 1601-01-01 when it has no answer, and a date
    /// four centuries ago rendered as "staged 425 years ago" is worse than an absent one.
    /// </remarks>
    private static DateTimeOffset? CreationTimeOf(string path)
    {
        try
        {
            var created = File.GetCreationTimeUtc(path);

            return created.Year > 1601 ? new DateTimeOffset(created, TimeSpan.Zero) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scans every stem root for provenance and pairs it against the ledger.
    /// </summary>
    /// <param name="stemRoots">
    /// Every folder a finished stem might live in — staging <em>and</em> the destination it
    /// gets moved to. A track whose stems have left staging is still processed.
    /// </param>
    public static ProcessedTracks Scan(KeyLedger ledger, IEnumerable<string> stemRoots)
    {
        var stemsByVideoId = new Dictionary<string, List<StemOnDisk>>(StringComparer.Ordinal);

        foreach (var root in stemRoots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                // Roughly half the files under a stem root are .asd sidecars, and asking
                // TagLib to open one throws — hundreds of exceptions per scan is an expensive
                // way to learn a file is not audio.
                if (!StagingBoard.IsStemFile(path))
                    continue;

                if (StemTagReader.Read(path)?.Provenance is not { SourceVideoId: { } videoId } p)
                    continue;

                if (!stemsByVideoId.TryGetValue(videoId, out var found))
                    stemsByVideoId[videoId] = found = [];

                // The root and the preset are already in hand here. Keeping them costs nothing
                // and is what lets a caller ask which root a file ended up in — the signal that
                // says the user has finished with it.
                found.Add(
                    new StemOnDisk
                    {
                        Path = path,
                        Root = root,
                        Preset = p.Preset,
                        CreatedAt = CreationTimeOf(path),
                    }
                );
            }
        }

        var withVerdict = ledger
            .Runs.Select(run => run.SourceVideoId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return new ProcessedTracks(
            stemsByVideoId,
            withVerdict.Where(stemsByVideoId.ContainsKey).ToHashSet(StringComparer.Ordinal),
            withVerdict
                .Where(id => !stemsByVideoId.ContainsKey(id))
                .ToHashSet(StringComparer.Ordinal)
        );
    }
}

/// <summary>One file carrying a track's provenance, and where the scan found it.</summary>
public sealed record StemOnDisk
{
    public required string Path { get; init; }

    /// <summary>
    /// The stem root it was found under.
    /// </summary>
    /// <remarks>
    /// Which root a file is in is the completion signal: a sample that has reached
    /// <c>Mashup Samples</c> is one the user has warped, tagged and finished with. Nothing is
    /// marked done — things simply move, which is what keeps this derived
    /// (<c>docs/adr/0002</c>).
    /// </remarks>
    public required string Root { get; init; }

    /// <summary>
    /// The StemForge preset — <c>Vocal - Full</c>, <c>Drums</c>. Null for a verbatim source
    /// download, which is how an instrumental release's kept sample reads.
    /// </summary>
    public string? Preset { get; init; }

    /// <summary>
    /// When the file appeared in <see cref="Root"/>, which is when it arrived rather than when it
    /// was made.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-02 on three samples the user had just moved: their creation time in
    /// <c>Mashup Samples</c> was <em>later</em> than their last-write time, so a move between
    /// these roots copies and deletes rather than renaming, and creation time marks arrival in the
    /// folder. That is what makes it usable for "staged three weeks ago" (ADR 0008).
    /// </remarks>
    public DateTimeOffset? CreatedAt { get; init; }
}
