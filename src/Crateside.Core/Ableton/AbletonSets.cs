using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ableton;

/// <summary>The user's Ableton sets, read for which samples they use by path (ADR 0039).</summary>
public static partial class AbletonSets
{
    private const string BackupFolder = "Backup";

    [GeneratedRegex("""<Path Value="(?<Path>[^"]*)""")]
    private static partial Regex FilePath { get; }

    /// <summary>
    /// The names of the sets under <paramref name="projectsRoot"/> that use any of
    /// <paramref name="samples"/>. Ableton's own backups are left out.
    /// </summary>
    public static IReadOnlyList<string> Using(
        string projectsRoot,
        IReadOnlyCollection<string> samples
    )
    {
        if (samples.Count is 0 || !Directory.Exists(projectsRoot))
            return [];

        var wanted = samples.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Directory
                .EnumerateFiles(projectsRoot, "*.als", SearchOption.AllDirectories)
                .Where(set => !IsBackup(projectsRoot, set))
                .Where(set => PathsIn(set).Any(wanted.Contains))
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static IEnumerable<string> PathsIn(string set)
    {
        string xml;

        try
        {
            using var file = File.OpenRead(set);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            xml = reader.ReadToEnd();
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return [];
        }

        return FilePath
            .Matches(xml)
            .Select(match => Normalize(WebUtility.HtmlDecode(match.Groups["Path"].Value)));
    }

    private static bool IsBackup(string root, string set) =>
        Path.GetRelativePath(root, set)
            .Split(Path.DirectorySeparatorChar)
            .Contains(BackupFolder, StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/');
}
