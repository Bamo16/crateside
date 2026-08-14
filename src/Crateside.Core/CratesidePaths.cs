using Crateside.Core.Orchestration;

namespace Crateside.Core;

/// <summary>
/// Where crateside looks for the things it reads. Defaults are this machine's real paths;
/// they are settable so a test — or another machine — is not stuck with them.
/// </summary>
/// <remarks>
/// <para>
/// The middle ground between config and constants: bindable from <c>appsettings.json</c>, but
/// with working defaults so nothing has to be configured to run.
/// </para>
/// <para>
/// crateside reads all of these. It writes exactly one thing: the
/// <c>Ableton Folder Info</c> XMP under <see cref="SampleStaging"/>, and only there
/// (ADR 0003). <see cref="MashupSamples"/> is the finished library and is never written —
/// it holds hand-applied tags crateside knows nothing about. The stem destinations are
/// where it <em>directs</em> the separator's output, not somewhere it puts files itself.
/// </para>
/// </remarks>
public sealed class CratesidePaths
{
    public const string SectionName = "Paths";

    private static readonly string MusicStems = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "Stems"
    );

    /// <summary>Where freshly separated stems land, awaiting the human-limited steps.</summary>
    public string SampleStaging { get; set; } = @"P:\RYAN\Ableton\Sample Staging";

    /// <summary>Where stems go once the work on them is finished.</summary>
    public string MashupSamples { get; set; } = @"P:\RYAN\Ableton\Mashup Samples";

    /// <summary>keytag's append-only record of detect-key runs. Read-only; keytag owns it.</summary>
    public string Ledger { get; set; } = @"V:\Repos\keytag\results\runs.jsonl";

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
