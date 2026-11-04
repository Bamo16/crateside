using Crateside.Core.Orchestration;

namespace Crateside.Core;

/// <summary>
/// Where crateside looks for the things it reads. Staging and Library start empty and are
/// chosen in Settings (ADR 0023).
/// </summary>
public sealed class CratesidePaths
{
    public const string SectionName = "Paths";

    private static readonly string MusicStems = Environment.SpecialFolder.MyMusic.GetFolderPath(
        "Stems"
    );

    /// <summary>
    /// When set, every folder and file crateside keeps its own state in, and the default Staging,
    /// Library and Ableton folders, sit under it instead of under the user's profile (ADR 0074).
    /// A path set by name still wins.
    /// </summary>
    public string DataRoot { get; set; } = string.Empty;

    /// <summary>The ffmpeg that reads beats: on PATH unless a build is pinned.</summary>
    public string BeatdetectFfmpeg { get; set; } = "ffmpeg";

    /// <summary>Where freshly separated stems land, awaiting the human-limited steps.</summary>
    public string StagingFolder
    {
        get => field is { Length: > 0 } chosen ? chosen : Rooted("Staging");
        set;
    } = string.Empty;

    /// <summary>Where stems go once the work on them is finished.</summary>
    public string LibraryFolder
    {
        get => field is { Length: > 0 } chosen ? chosen : Rooted("Library");
        set;
    } = string.Empty;

    /// <summary>
    /// Optional: the folder holding the user's Ableton sets, relinked when a track is renamed;
    /// empty means renames relink no sets.
    /// </summary>
    public string AbletonProjects
    {
        get => field is { Length: > 0 } chosen ? chosen : Rooted("Ableton");
        set;
    } = string.Empty;

    /// <summary>
    /// The folder holding Ableton's own file index, the database behind the browser. Read-only;
    /// Ableton owns it and holds it open.
    /// </summary>
    public string AbletonIndex
    {
        get =>
            field
            ?? (
                DataRoot is { Length: > 0 } root
                    ? Path.Combine(root, "state", "ableton-index")
                    : Environment.SpecialFolder.LocalApplicationData.GetFolderPath(
                        "Ableton",
                        "Live Database"
                    )
            );
        set;
    }

    /// <summary>The ffmpeg that decodes for madmom and edma, and the keydetect build a machine knowing none starts on.</summary>
    public string KeydetectFfmpeg { get; set; } = "ffmpeg";

    /// <summary>crateside's ledger of key verdicts, a SQLite file (docs/ledger.md).</summary>
    public string LedgerDatabase
    {
        get => field ?? Local("ledger.db");
        set;
    }

    /// <summary>The ffmpeg builds crateside knows for each filter, and the one it pins for each.</summary>
    public string KnownBuildsFile
    {
        get => field ?? Roaming("builds.json");
        set;
    }

    /// <summary>The beats beatdetect has read, kept across restarts.</summary>
    public string BeatCache
    {
        get => field ?? Roaming("beats.json");
        set;
    }

    /// <summary>The tempos read from warped clips, kept across restarts.</summary>
    public string TempoCache
    {
        get => field ?? Roaming("tempos.json");
        set;
    }

    /// <summary>The tags read off stem files, kept across restarts so a start does not open every file again.</summary>
    public string StemTagCache
    {
        get => field ?? Local("stem-tags.json");
        set;
    }

    /// <summary>Videos YouTube reports gone or private, so their tracks stop counting as missing sources.</summary>
    public string UnavailableSourcesFile
    {
        get => field ?? Local("unavailable-sources.json");
        set;
    }

    /// <summary>The songs owed a fresh judgment.</summary>
    public string RejudgeFile
    {
        get => field ?? Roaming("rejudge.json");
        set;
    }

    /// <summary>The marks the user has put on songs.</summary>
    public string TrackMarksFile
    {
        get => field ?? Roaming("track-marks.json");
        set;
    }

    /// <summary>The daily log files agents read instead of the terminal.</summary>
    public string LogsFolder
    {
        get => field ?? Local("logs");
        set;
    }

    /// <summary>The loudness ffmpeg has read, kept across restarts.</summary>
    public string LoudnessCache
    {
        get => field ?? Roaming("loudness.json");
        set;
    }

    /// <summary>The sidecars crateside wrote, so it knows which are still its own.</summary>
    public string WrittenWarpsFile
    {
        get => field ?? Roaming("written-warps.json");
        set;
    }

    /// <summary>The sidecars crateside retyped, so it knows which retypes still stand.</summary>
    public string RetypedWarpsFile
    {
        get => field ?? Roaming("retyped-warps.json");
        set;
    }

    /// <summary>Where the transients crateside wrote into sidecars came from.</summary>
    public string TransientOriginsFile
    {
        get => field ?? Roaming("transients.json");
        set;
    }

    /// <summary>Samples waiting for Live to analyze them afresh.</summary>
    public string LiveAnalysesFile
    {
        get => field ?? Roaming("live-analyses.json");
        set;
    }

    /// <summary>Copies of sidecars taken before crateside replaced or recycled them.</summary>
    public string WarpBackups
    {
        get => field ?? Roaming("backups", "warps");
        set;
    }

    /// <summary>Copies of tag stores taken before crateside wrote them.</summary>
    public string TagStoreBackups
    {
        get => field ?? Roaming("backups", "tag-stores");
        set;
    }

    /// <summary>Copies of files taken before a one-time startup step rewrote them.</summary>
    public string SpellingBackups
    {
        get => field ?? Roaming("backups", "spelling");
        set;
    }

    /// <summary>Where stems go when the track has a key the user would tag with.</summary>
    public string StemsKeyIdentified
    {
        get => field ?? Stems("Key Identified");
        set;
    }

    /// <summary>Where stems go when it does not — including when it has not been judged yet.</summary>
    public string StemsUnresolvedKey
    {
        get => field ?? Stems("Unresolved Key");
        set;
    }

    /// <summary>Where warp guides are kept once their track is filed: outside every folder Ableton browses (ADR 0046).</summary>
    public string KeptWarpGuides
    {
        get => field ?? Stems("Drums");
        set;
    }

    /// <summary>Each song's genre, as the user settled it and as the sources suggested it.</summary>
    public string GenresFile
    {
        get => field ?? Roaming("genres.json");
        set;
    }

    /// <summary>Each song's familiarity, as Deezer measured it and as the user settled it.</summary>
    public string FamiliarityFile
    {
        get => field ?? Roaming("familiarity.json");
        set;
    }

    /// <summary>The names StemForge has resolved.</summary>
    public string ResolvedNamesFile
    {
        get => field ?? Roaming("names.json");
        set;
    }

    /// <summary>Everything the user did to a track.</summary>
    public string ActionsFile
    {
        get => field ?? Roaming("actions.jsonl");
        set;
    }

    /// <summary>Every sample crateside recycled on its own initiative.</summary>
    public string DeletionsFile
    {
        get => field ?? Roaming("deletions.jsonl");
        set;
    }

    /// <summary>The record of the work crateside has run.</summary>
    public string JobsFile
    {
        get => field ?? Roaming("jobs.jsonl");
        set;
    }

    /// <summary>What a pass with no job record did to a song.</summary>
    public string StageNotesFile
    {
        get => field ?? Roaming("stage-notes.jsonl");
        set;
    }

    /// <summary>Whether the user paused separation or judging.</summary>
    public string PausedFile
    {
        get => field ?? Roaming("paused.json");
        set;
    }

    /// <summary>Every source on disk, found by video ID or song.</summary>
    public string SourceIndexFile
    {
        get => field ?? Local("source-index.json");
        set;
    }

    /// <summary>How long downloads are kept.</summary>
    public string SourceRetentionFile
    {
        get => field ?? Roaming("source-retention.json");
        set;
    }

    /// <summary>The judging choices made in Settings.</summary>
    public string JudgeSettingsFile
    {
        get => field ?? Roaming("judge-settings.json");
        set;
    }

    /// <summary>Where each track's downloaded source and evidence are kept while it is worked on.</summary>
    public string WorkingFolder
    {
        get => field ?? Local("working");
        set;
    }

    /// <summary>The audio of tracks that are not on YouTube, one folder per track key; never pruned (ADR 0077).</summary>
    public string ImportsFolder
    {
        get => field ?? Local("imports");
        set;
    }

    /// <summary>The cover art taken from imported files and sites, one file per track key.</summary>
    public string CoversFolder
    {
        get => field ?? Local("covers");
        set;
    }

    /// <summary>The folder crateside consumes: audio files and zips saved here become tracks (ADR 0077).</summary>
    public string Inbox
    {
        get =>
            field is { Length: > 0 } chosen ? chosen
            : DataRoot is { Length: > 0 } root ? Path.Combine(root, "Inbox")
            : Environment.SpecialFolder.MyMusic.GetFolderPath("crateside inbox");
        set;
    } = string.Empty;

    /// <summary>The folders the user changed in Settings.</summary>
    public string PathOverridesFile
    {
        get => field ?? Roaming("paths.json");
        set;
    }

    /// <summary>The recipe a new track is taken in with, chosen in Settings.</summary>
    public string RecipeFile
    {
        get => field ?? Roaming("recipe.json");
        set;
    }

    /// <summary>The playlist entries crateside has taken on.</summary>
    public string IntakeFile
    {
        get => field ?? Roaming("intake.json");
        set;
    }

    /// <summary>The encrypted playlist credential.</summary>
    public string TokensFolder
    {
        get => field ?? Roaming("tokens");
        set;
    }

    /// <summary>The data protection key ring.</summary>
    public string KeyRingFolder
    {
        get => field ?? Local("keys");
        set;
    }

    /// <summary>The spend-down chosen in Settings.</summary>
    public string SpendDownFile
    {
        get => field ?? Roaming("spend-down.json");
        set;
    }

    /// <summary>The starter-warp setting chosen in Settings.</summary>
    public string WarpSettingsFile
    {
        get => field ?? Roaming("warp-settings.json");
        set;
    }

    /// <summary>The playlist settings and the track instructions.</summary>
    public string QueueFile
    {
        get => field ?? Roaming("queue.json");
        set;
    }

    /// <summary>Where Claude Code keeps its login, which the quota is read with.</summary>
    public string ClaudeCredentialsFile
    {
        get =>
            field
            ?? (
                DataRoot is { Length: > 0 } root
                    ? Path.Combine(root, "state", "claude-credentials.json")
                    : Environment.SpecialFolder.UserProfile.GetFolderPath(
                        ".claude",
                        ".credentials.json"
                    )
            );
        set;
    }

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

    private string Roaming(params ReadOnlySpan<string> parts) =>
        Under(Environment.SpecialFolder.ApplicationData, parts);

    private string Local(params ReadOnlySpan<string> parts) =>
        Under(Environment.SpecialFolder.LocalApplicationData, parts);

    private string Under(Environment.SpecialFolder folder, ReadOnlySpan<string> parts) =>
        DataRoot is { Length: > 0 } root
            ? Path.Combine([root, "state", .. parts])
            : folder.GetFolderPath(["crateside", .. parts]);

    private string Stems(string name) =>
        DataRoot is { Length: > 0 } root
            ? Path.Combine(root, "Stems", name)
            : Path.Combine(MusicStems, name);

    private string Rooted(string name) =>
        DataRoot is { Length: > 0 } root ? Path.Combine(root, name) : string.Empty;
}
