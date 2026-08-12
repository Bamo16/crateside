namespace Crateside.Core;

/// <summary>
/// Where crateside looks for the things it reads. Defaults are this machine's real paths;
/// they are settable so a test — or another machine — is not stuck with them.
/// </summary>
/// <remarks>
/// The handoff left "config vs constants" as an unmade v1 call. This is the middle: bindable
/// from <c>appsettings.json</c>, but with working defaults so nothing has to be configured to
/// run. crateside never writes to any of these.
/// </remarks>
public sealed class CratesidePaths
{
    public const string SectionName = "Paths";

    /// <summary>Where freshly separated stems land, awaiting the human-limited steps.</summary>
    public string SampleStaging { get; set; } = @"P:\RYAN\Ableton\Sample Staging";

    /// <summary>Where stems go once the work on them is finished.</summary>
    public string MashupSamples { get; set; } = @"P:\RYAN\Ableton\Mashup Samples";

    /// <summary>keytag's append-only record of detect-key runs. Read-only; keytag owns it.</summary>
    public string Ledger { get; set; } = @"V:\Repos\keytag\results\runs.jsonl";
}
