using System.Runtime.CompilerServices;

namespace Crateside.Tests;

/// <summary>Where the repository's own files are, found from this source file's path.</summary>
internal static class Repository
{
    public static string Root([CallerFilePath] string here = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(here)!);

        while (!File.Exists(Path.Combine(directory.FullName, "Crateside.slnx")))
            directory = directory.Parent!;

        return directory.FullName;
    }
}
