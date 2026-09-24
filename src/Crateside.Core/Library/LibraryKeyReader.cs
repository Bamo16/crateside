using Crateside.Core.Genres;
using Crateside.Core.Pairing;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Library;

/// <summary>
/// How many files in the finished library carry each key, counted from Ableton's index
/// (ADR 0013).
/// </summary>
public sealed class LibraryKeyReader(
    IOptions<CratesidePaths> paths,
    AbletonIndexReader index,
    AbletonFolderInfoReader folderInfo
)
{
    private readonly CratesidePaths _paths = paths.Value;

    public KeyDistribution Read()
    {
        var root = _paths.MashupSamples;

        if (!Directory.Exists(root))
            return KeyDistribution.Empty(root);

        var tags = index.Read(root);
        var backed = folderInfo.Read(root);
        var counts = new Dictionary<MusicKey, int>();
        var genres = new Dictionary<string, int>(StringComparer.Ordinal);
        var unkeyed = new List<string>();
        var files = 0;
        var perishable = 0;

        foreach (
            var file in Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(StagingBoard.IsStemFile)
        )
        {
            files++;

            if (tags.TryGet(file, out var tagged) && tagged.Genre is { } genre)
                genres[genre] = genres.GetValueOrDefault(genre) + 1;

            if (tags.TryGet(file, out var applied) && MusicKey.FromString(applied.Key) is { } key)
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;

                if (!backed.TryGet(file, out _))
                    perishable++;
            }
            else
                unkeyed.Add(Path.GetRelativePath(root, file));
        }

        return new KeyDistribution
        {
            Root = root,
            Counts =
            [
                .. MusicKey.AllKeys.Select(k => new KeyCount(k, counts.GetValueOrDefault(k))),
            ],
            FileCount = files,
            Genres =
            [
                .. GenreVocabulary.All.Select(g => new GenreCount(g, genres.GetValueOrDefault(g))),
            ],
            Unkeyed = [.. unkeyed.Order(StringComparer.OrdinalIgnoreCase)],
            PerishableCount = perishable,
        };
    }
}

/// <summary>What the library holds, by key, at one moment.</summary>
public sealed record KeyDistribution
{
    public required string Root { get; init; }

    /// <summary>All 24 keys, always — a key with no files is worth showing as a zero.</summary>
    public required IReadOnlyList<KeyCount> Counts { get; init; }

    public required int FileCount { get; init; }

    /// <summary>Every genre on the list, a zero included, from the same scan (ADR 0030).</summary>
    public IReadOnlyList<GenreCount> Genres { get; init; } = [];

    /// <summary>Files with no genre yet — not looked at, which is not the same as having none.</summary>
    public int UngenredCount => FileCount - Genres.Sum(g => g.Files);

    /// <summary>
    /// Files carrying no key anywhere Ableton can see, named rather than merely counted:
    /// three of them is a list the user can act on.
    /// </summary>
    public required IReadOnlyList<string> Unkeyed { get; init; }

    /// <summary>
    /// Keyed files with no tag-store entry, whose key the next write to this root destroys
    /// (ADR 0023).
    /// </summary>
    public int PerishableCount { get; init; }

    public int KeyedCount => FileCount - Unkeyed.Count;

    /// <summary>The largest bucket, for scaling a bar against.</summary>
    public int Largest => Counts.Count is 0 ? 0 : Counts.Max(c => c.Files);

    public static KeyDistribution Empty(string root) =>
        new()
        {
            Root = root,
            Counts = [],
            FileCount = 0,
            Unkeyed = [],
        };
}

public sealed record KeyCount(MusicKey Key, int Files);

public sealed record GenreCount(string Genre, int Files);
