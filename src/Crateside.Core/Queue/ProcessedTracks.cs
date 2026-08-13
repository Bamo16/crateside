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
    private readonly Dictionary<string, List<string>> _stemsByVideoId;

    private ProcessedTracks(
        Dictionary<string, List<string>> stemsByVideoId,
        IReadOnlySet<string> fullyProcessed,
        IReadOnlySet<string> verdictWithoutStems
    )
    {
        _stemsByVideoId = stemsByVideoId;
        FullyProcessed = fullyProcessed;
        VerdictWithoutStems = verdictWithoutStems;
    }

    /// <summary>Video IDs that satisfy both halves of the test.</summary>
    public IReadOnlySet<string> FullyProcessed { get; }

    /// <summary>
    /// Video IDs with a verdict but no stem anywhere on disk — a separation that never
    /// finished, or stems the user deleted. These deliberately stay queued.
    /// </summary>
    public IReadOnlySet<string> VerdictWithoutStems { get; }

    /// <summary>Where the stems for a video ID live. Empty when none are on disk.</summary>
    public IReadOnlyList<string> StemsFor(string videoId) =>
        _stemsByVideoId.TryGetValue(videoId, out var stems) ? stems : [];

    public bool IsFullyProcessed(string videoId) => FullyProcessed.Contains(videoId);

    /// <summary>
    /// Scans every stem root for provenance and pairs it against the ledger.
    /// </summary>
    /// <param name="stemRoots">
    /// Every folder a finished stem might live in — staging <em>and</em> the destination it
    /// gets moved to. A track whose stems have left staging is still processed.
    /// </param>
    public static ProcessedTracks Scan(
        KeyLedger ledger,
        IEnumerable<string> stemRoots,
        StemTagReader? tagReader = null
    )
    {
        var reader = tagReader ?? new StemTagReader();
        var stemsByVideoId = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var root in stemRoots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                // Roughly half the files under a stem root are .asd sidecars, and asking
                // TagLib to open one throws — hundreds of exceptions per scan is an expensive
                // way to learn a file is not audio.
                if (!StagingBoard.IsStemFile(path))
                    continue;

                if (reader.Read(path)?.SourceVideoId is not { } videoId)
                    continue;

                if (!stemsByVideoId.TryGetValue(videoId, out var paths))
                    stemsByVideoId[videoId] = paths = [];

                paths.Add(path);
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
