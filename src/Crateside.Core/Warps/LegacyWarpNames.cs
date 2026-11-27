namespace Crateside.Core.Warps;

/// <summary>The property names the stores wrote while an auto warp was still called a starter (ADR 0114).</summary>
public static class LegacyWarpNames
{
    internal static readonly IReadOnlyDictionary<string, string> StoredKeys = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = "autoWarp",
        ["isStarter"] = "isAutoWarp",
        ["wasStarter"] = "wasAutoWarp",
        ["fromStarter"] = "fromAutoWarp",
        ["starterWarps"] = "autoWarps",
    };

    /// <summary>The <c>Warps:StarterWarps</c> setting an appsettings file or environment variable may still carry; null when absent or not a mode.</summary>
    public static AutoWarps? AutoWarpsFromOldSetting(string? value) =>
        Enum.TryParse<AutoWarps>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : null;
}
