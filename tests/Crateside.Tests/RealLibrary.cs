using Crateside.Core.Staging;

namespace Crateside.Tests;

/// <summary>
/// Whether this machine's real roots hold anything, for the tests that assert against them.
/// </summary>
/// <remarks>
/// A root that exists and is empty is an ordinary state, not a missing machine: staging empties
/// every time the user finishes what is in it. So these tests skip on "no stems here" rather
/// than on "no folder here", which is what they meant all along.
/// </remarks>
internal static class RealLibrary
{
    internal static bool HasStems(string root) =>
        Directory.Exists(root)
        && Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Any(StagingBoard.IsStemFile);
}
