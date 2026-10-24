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
        @"V:\Repos\beatdetect-harness\builds\1.3.1-48ba578\ffmpeg.exe";

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

    /// <summary>The ffmpeg that decodes for madmom and edma, and the keydetect build a machine knowing none starts on.</summary>
    public string KeydetectFfmpeg { get; set; } = "ffmpeg";

    /// <summary>crateside's ledger of key verdicts, a SQLite file (docs/ledger.md).</summary>
    public string LedgerDatabase { get; set; } =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("crateside", "ledger.db");

    /// <summary>The ffmpeg builds crateside knows for each filter, and the one it pins for each.</summary>
    public string KnownBuildsFile { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "builds.json");

    /// <summary>The beats beatdetect has read, kept across restarts.</summary>
    public string BeatCache { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "beats.json");

    /// <summary>The tempos read from warped clips, kept across restarts.</summary>
    public string TempoCache { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "tempos.json");

    /// <summary>The tags read off stem files, kept across restarts so a start does not open every file again.</summary>
    public string StemTagCache { get; set; } =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("crateside", "stem-tags.json");

    /// <summary>Videos YouTube reports gone or private, so their tracks stop counting as missing sources.</summary>
    public string UnavailableSourcesFile { get; set; } =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath(
            "crateside",
            "unavailable-sources.json"
        );

    /// <summary>The songs owed a fresh judgment.</summary>
    public string RejudgeFile { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "rejudge.json");

    /// <summary>The marks the user has put on songs.</summary>
    public string TrackMarksFile { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "track-marks.json");

    /// <summary>The daily log files agents read instead of the terminal.</summary>
    public string LogsFolder { get; set; } =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("crateside", "logs");

    /// <summary>The loudness ffmpeg has read, kept across restarts.</summary>
    public string LoudnessCache { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "loudness.json");

    /// <summary>The sidecars crateside wrote, so it knows which are still its own.</summary>
    public string WrittenWarpsFile { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "written-warps.json");

    /// <summary>The sidecars crateside retyped, so it knows which retypes still stand.</summary>
    public string RetypedWarpsFile { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "retyped-warps.json");

    /// <summary>Where the transients crateside wrote into sidecars came from.</summary>
    public string TransientOriginsFile { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "transients.json");

    /// <summary>Samples waiting for Live to analyse them afresh.</summary>
    public string LiveAnalysesFile { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "live-analyses.json");

    /// <summary>Copies of sidecars taken before crateside replaced or recycled them.</summary>
    public string WarpBackups { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "backups", "warps");

    /// <summary>Copies of tag stores taken before crateside wrote them.</summary>
    public string TagStoreBackups { get; set; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "crateside",
            "backups",
            "tag-stores"
        );

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
