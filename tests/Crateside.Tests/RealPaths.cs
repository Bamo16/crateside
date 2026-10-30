using Crateside.Core;
using Crateside.Core.Orchestration.Loop;

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

    /// <summary>The build crateside pins for the filter on this machine (ADR 0066); skips the test when there is none.</summary>
    public static KnownBuild PinnedBuild(FfmpegFilter filter)
    {
        var pinned = KnownBuilds().Pinned(filter);

        Assert.SkipUnless(
            pinned is { Path: var path } && File.Exists(path),
            $"No {FfmpegBuild.NameOf(filter)} build is pinned on this machine."
        );

        return pinned;
    }

    /// <summary>The builds crateside knows for the filter whose executables are still here.</summary>
    public static IReadOnlyList<KnownBuild> BuildsOf(FfmpegFilter filter) =>
        [.. KnownBuilds().All(filter).Where(known => File.Exists(known.Path))];

    private static string Chosen(string folder, string what)
    {
        Assert.SkipUnless(
            Directory.Exists(folder),
            $"No {what} is chosen in Settings, or it is not on this machine."
        );

        return folder;
    }

    private static KnownBuilds KnownBuilds() => new(new ProcessRunner(), Create());
}
