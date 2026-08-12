namespace Crateside.Core.Staging;

/// <summary>
/// Whether a stem has been warped in Ableton, which is true exactly when a <c>.asd</c>
/// sidecar sits next to it.
/// </summary>
/// <remarks>
/// Warped and tagged are independent: real data has a song tagged but unwarped, and another
/// that is both. The staging board reports them as two separate columns for that reason.
/// </remarks>
public static class WarpMarkers
{
    private const string SidecarExtension = ".asd";

    /// <summary>
    /// The sidecar path for a stem. Ableton appends <c>.asd</c> to the <em>full</em> filename
    /// rather than replacing the extension — <c>song.flac</c> pairs with <c>song.flac.asd</c>,
    /// not <c>song.asd</c>.
    /// </summary>
    public static string SidecarPathFor(string stemPath) => stemPath + SidecarExtension;

    /// <summary>True when the stem has a warp sidecar on disk.</summary>
    public static bool IsWarped(string stemPath) => File.Exists(SidecarPathFor(stemPath));
}
