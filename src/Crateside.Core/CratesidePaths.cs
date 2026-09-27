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

    /// <summary>Where freshly separated stems land, awaiting the human-limited steps.</summary>
    public string SampleStaging { get; set; } = @"P:\RYAN\Ableton\Sample Staging";

    /// <summary>Where stems go once the work on them is finished.</summary>
    public string MashupSamples { get; set; } = @"P:\RYAN\Ableton\Mashup Samples";

    /// <summary>Where the user's Ableton sets live, read for the samples they use.</summary>
    public string AbletonProjects { get; set; } = @"P:\RYAN\Ableton\Projects";

    /// <summary>
    /// The folder holding Ableton's own file index, the database behind the browser. Read-only;
    /// Ableton owns it and holds it open.
    /// </summary>
    public string AbletonIndex { get; set; } =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("Ableton", "Live Database");

    private string? _ledger;

    /// <summary>
    /// The keytag checkout, reached only through <c>keytag-detect</c> for the evidence run and
    /// <c>runs.jsonl</c> for the verdict (keytag ADR 0006).
    /// </summary>
    public string KeytagRepo { get; set; } = @"V:\Repos\keytag";

    /// <summary>keytag's append-only record of detect-key runs. Read-only; keytag owns it.</summary>
    public string Ledger
    {
        get => _ledger ?? Path.Combine(KeytagRepo, "results", "runs.jsonl");
        set => _ledger = value;
    }

    /// <summary>Where stems go when the track has a key the user would tag with.</summary>
    public string StemsKeyIdentified { get; set; } = Path.Combine(MusicStems, "Key Identified");

    /// <summary>Where stems go when it does not — including when it has not been judged yet.</summary>
    public string StemsUnresolvedKey { get; set; } = Path.Combine(MusicStems, "Unresolved Key");

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
