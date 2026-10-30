using Crateside.Core;

namespace Crateside.Tests;

/// <summary>The paths of the machine running the tests: the folders chosen in Settings, over the defaults.</summary>
internal static class RealPaths
{
    public static CratesidePaths Create()
    {
        var chosen = new PathOverrideStore().Read();

        return new()
        {
            StagingFolder = chosen.StagingFolder ?? string.Empty,
            LibraryFolder = chosen.LibraryFolder ?? string.Empty,
            AbletonProjects = chosen.AbletonProjects ?? string.Empty,
        };
    }
}
