using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ableton;

/// <summary>The user's Ableton sets, read and relinked for the samples they use by path (ADR 0040).</summary>
public static partial class AbletonSets
{
    private const string BackupFolder = "Backup";

    private static readonly ConcurrentDictionary<string, Referenced> Cache = new(
        StringComparer.OrdinalIgnoreCase
    );

    [GeneratedRegex("""<Path Value="(?<Path>[^"]*)""")]
    private static partial Regex FilePath { get; }

    /// <summary>A sample reference: the name its relative path ends in, then its full path.</summary>
    [GeneratedRegex(
        """
            <FileRef(\s[^>]*)?>
            .*?
            <RelativePath\sValue="([^"]*/)?(?<Name>[^"/]*)"
            .*?
            <Path\sValue="(?<Path>[^"]*)"
            """,
        RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex FileRef { get; }

    /// <summary>
    /// The names of the sets under <paramref name="projectsRoot"/> that use any of
    /// <paramref name="samples"/>. Ableton's own backups are left out.
    /// </summary>
    public static IReadOnlyList<string> Using(
        string projectsRoot,
        IReadOnlyCollection<string> samples
    ) =>
        [
            .. SetsUsing(projectsRoot, samples)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

    /// <summary>
    /// Points every set that uses a moved sample at its new name, keeping each set as it was in its
    /// project's <c>Backup</c> folder first.
    /// </summary>
    public static SetRelink Relink(
        string projectsRoot,
        IReadOnlyList<SampleMove> moves,
        DateTimeOffset now
    )
    {
        var renamed = moves
            .DistinctBy(move => Normalize(move.From), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                move => Normalize(move.From),
                move => Normalize(move.To),
                StringComparer.OrdinalIgnoreCase
            );

        List<string> relinked = [];
        List<string> failed = [];

        foreach (var set in SetsUsing(projectsRoot, renamed.Keys))
        {
            var name = Path.GetFileNameWithoutExtension(set);

            try
            {
                var xml = Read(set) ?? throw new InvalidDataException($"{name} could not be read.");

                Backup(set, now);
                Write(set, Relinked(xml, renamed));
                relinked.Add(name);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failed.Add(name);
            }
        }

        return new SetRelink(relinked, failed);
    }

    private static List<string> SetsUsing(string projectsRoot, IReadOnlyCollection<string> samples)
    {
        if (samples.Count is 0 || !Directory.Exists(projectsRoot))
            return [];

        var wanted = samples.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Directory
                .EnumerateFiles(projectsRoot, "*.als", SearchOption.AllDirectories)
                .Where(set => !IsBackup(projectsRoot, set))
                .Where(set => PathsIn(set).Overlaps(wanted)),
        ];
    }

    /// <summary>Replaces the name in the relative path and the whole full path, and nothing else.</summary>
    private static string Relinked(string xml, Dictionary<string, string> renamed) =>
        FileRef.Replace(
            xml,
            match =>
            {
                var from = Normalize(WebUtility.HtmlDecode(match.Groups["Path"].Value));

                if (!renamed.TryGetValue(from, out var to))
                    return match.Value;

                var (name, path) = (match.Groups["Name"], match.Groups["Path"]);
                var nameAt = name.Index - match.Index;
                var pathAt = path.Index - match.Index;
                var value = match.Value;

                return $"{value[..nameAt]}{Escape(Path.GetFileName(to))}"
                    + $"{value[(nameAt + name.Length)..pathAt]}{Escape(to)}"
                    + value[(pathAt + path.Length)..];
            }
        );

    private static void Backup(string set, DateTimeOffset now)
    {
        var folder = Path.Combine(Path.GetDirectoryName(set)!, BackupFolder);
        Directory.CreateDirectory(folder);

        File.Copy(
            set,
            Path.Combine(
                folder,
                $"{Path.GetFileNameWithoutExtension(set)} [crateside {now:yyyy-MM-dd HHmmss}].als"
            )
        );
    }

    private static string? Read(string set)
    {
        try
        {
            using var file = File.OpenRead(set);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            return reader.ReadToEnd();
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes beside the set and swaps it in, so a failure leaves the set as it was.</summary>
    private static void Write(string set, string xml)
    {
        var temporary = $"{set}.crateside";

        using (var file = File.Create(temporary))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            gzip.Write(new UTF8Encoding(false).GetBytes(xml));

        File.Move(temporary, set, overwrite: true);
    }

    /// <summary>Every sample path a set references, read again only when the set's stamp moves.</summary>
    private static IReadOnlySet<string> PathsIn(string set)
    {
        if (new FileInfo(set) is not { Exists: true } file)
            return new HashSet<string>();

        var stamp = (file.Length, file.LastWriteTimeUtc);

        if (Cache.TryGetValue(set, out var held) && held.Stamp == stamp)
            return held.Paths;

        if (Read(set) is not { } xml)
            return new HashSet<string>();

        var paths = FilePath
            .Matches(xml)
            .Select(match => Normalize(WebUtility.HtmlDecode(match.Groups["Path"].Value)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Cache[set] = new Referenced(stamp, paths);

        return paths;
    }

    private static bool IsBackup(string root, string set) =>
        Path.GetRelativePath(root, set)
            .Split(Path.DirectorySeparatorChar)
            .Contains(BackupFolder, StringComparer.OrdinalIgnoreCase);

    // Ableton escapes only the ampersand, the one XML special character a file name can hold.
    private static string Escape(string value) => value.Replace("&", "&amp;");

    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed record Referenced((long Length, DateTime Written) Stamp, HashSet<string> Paths);
}

/// <summary>A sample renamed on disk, from and to its full path.</summary>
public sealed record SampleMove(string From, string To);

/// <summary>Which sets now point at the new names, and which could not be changed.</summary>
public sealed record SetRelink(IReadOnlyList<string> Relinked, IReadOnlyList<string> Failed);
