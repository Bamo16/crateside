using Crateside.Core.Orchestration;

namespace Crateside.Core;

/// <summary>
/// Where crateside looks for the things it reads, defaulting to this machine's real paths
/// (ADR 0023).
/// </summary>
public sealed class CratesidePaths
{
    public const string SectionName = "Paths";

    private static readonly string MusicStems = Environment.SpecialFolder.MyMusic.GetFolderPath(
        "Stems"
    );

    /// <summary>The beatdetect build a machine knowing none starts on: the newest the harness has scored.</summary>
    public string BeatdetectFfmpeg { get; set; } =
        @"V:\Repos\beatdetect-harness\builds\1.3.0-1e57fec\ffmpeg.exe";

    /// <summary>Where freshly separated stems land, awaiting the human-limited steps.</summary>
    public string StagingFolder { get; set; } = @"P:\RYAN\Ableton\Sample Staging";

    /// <summary>Where stems go once the work on them is finished.</summary>
    public string LibraryFolder { get; set; } = @"P:\RYAN\Ableton\Mashup Samples";

    /// <summary>Where the user's Ableton sets live, read for the samples they use.</summary>
    public string AbletonProjects { get; set; } = @"P:\RYAN\Ableton\Projects";

    /// <summary>
    /// The folder holding Ableton's own file index, the database behind the browser. Read-only;
    /// Ableton owns it and holds it open.
    /// </summary>
    public string AbletonIndex { get; set; } =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("Ableton", "Live Database");

    private string? _keytagLedger;

    /// <summary>
    /// The keytag checkout, reached only through <c>keytag-detect</c> for the evidence run and
    /// <c>/detect-key</c> for the verdict (keytag ADR 0006).
    /// </summary>
    public string KeytagRepo { get; set; } = @"V:\Repos\keytag";

    /// <summary>keytag's <c>runs.jsonl</c>, imported into the ledger until the judge writes there itself (#111).</summary>
    public string KeytagLedger
    {
        get => _keytagLedger ?? Path.Combine(KeytagRepo, "results", "runs.jsonl");
        set => _keytagLedger = value;
    }

    /// <summary>crateside's ledger of key verdicts, a SQLite file (docs/ledger.md).</summary>
    public string LedgerDatabase { get; set; } =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("crateside", "ledger.db");

    /// <summary>Where stems go when the track has a key the user would tag with.</summary>
    public string StemsKeyIdentified { get; set; } = Path.Combine(MusicStems, "Key Identified");

    /// <summary>Where stems go when it does not — including when it has not been judged yet.</summary>
    public string StemsUnresolvedKey { get; set; } = Path.Combine(MusicStems, "Unresolved Key");

    /// <summary>Where warp guides are kept once their track is filed: outside every folder Ableton browses (ADR 0046).</summary>
    public string KeptWarpGuides { get; set; } = Path.Combine(MusicStems, "Drums");

    /// <summary>
    /// The folder a <see cref="StemDestination"/> names. Both already exist on this machine;
    /// crateside picks between them and never invents a sibling.
    /// </summary>
    public string DestinationFor(StemDestination destination) =>
        destination switch
        {
            StemDestination.KeyIdentified => StemsKeyIdentified,
            StemDestination.UnresolvedKey => StemsUnresolvedKey,
            _ => throw new ArgumentOutOfRangeException(nameof(destination)),
        };
}
