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

    /// <summary>The staging folder chosen in Settings; skips the test when none is chosen or it is not here.</summary>
    public static string Staging() => Chosen(Create().StagingFolder, "staging folder");

    /// <summary>The library folder chosen in Settings; skips the test when none is chosen or it is not here.</summary>
    public static string Library() => Chosen(Create().LibraryFolder, "library folder");

    private static string Chosen(string folder, string what)
    {
        Assert.SkipUnless(
            Directory.Exists(folder),
            $"No {what} is chosen in Settings, or it is not on this machine."
        );

        return folder;
    }
}
