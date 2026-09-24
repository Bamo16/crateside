using Crateside.Core.Staging;

namespace Crateside.Tests;

/// <summary>Whether this machine's real roots hold anything, for the tests that assert against them.</summary>
/// <remarks>Staging empties whenever the user finishes, so skip on "no stems", not "no folder".</remarks>
internal static class RealLibrary
{
    internal static bool HasStems(string root) =>
        Directory.Exists(root)
        && Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Any(StagingBoard.IsStemFile);
}
