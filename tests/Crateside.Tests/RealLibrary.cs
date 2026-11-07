using Crateside.Core.Samples;

namespace Crateside.Tests;

/// <summary>
/// Whether this machine's real roots hold stems, not merely exist — staging empties whenever the
/// user finishes.
/// </summary>
internal static class RealLibrary
{
    internal static bool HasStems(string root) =>
        Directory.Exists(root)
        && Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Any(SampleFiles.IsAudio);
}
