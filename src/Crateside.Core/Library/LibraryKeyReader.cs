using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Pairing;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Library;

/// <summary>
/// How many tracks in the finished library carry each key and genre, counted from Ableton's
/// index (ADR 0013): a song's samples and sources count once between them.
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
        var ledger = KeyLedger.Read(_paths.Ledger);
        var tracks = new Dictionary<string, List<AbletonFileTags?>>(
            StringComparer.OrdinalIgnoreCase
        );
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

            var applied = tags.TryGet(file, out var found) ? found : null;

            var track = TrackOf(file, ledger);

            if (!tracks.TryGetValue(track, out var samples))
                tracks[track] = samples = [];

            samples.Add(applied);

            if (MusicKey.FromString(applied?.Key) is null)
                unkeyed.Add(Path.GetRelativePath(root, file));
            else if (!backed.TryGet(file, out _))
                perishable++;
        }

        var keys = tracks.Values.Select(samples => MostCommon(samples, s => s?.Key)).ToArray();
        var genres = tracks.Values.Select(samples => MostCommon(samples, s => s?.Genre)).ToArray();

        return new KeyDistribution
        {
            Root = root,
            Counts =
            [
                .. MusicKey.AllKeys.Select(k => new KeyCount(
                    k,
                    keys.Count(key => k.Equals(MusicKey.FromString(key)))
                )),
            ],
            Genres =
            [
                .. GenreVocabulary.All.Select(g => new GenreCount(
                    g,
                    genres.Count(genre => genre == g)
                )),
            ],
            FileCount = files,
            TrackCount = tracks.Count,
            Unkeyed = [.. unkeyed.Order(StringComparer.OrdinalIgnoreCase)],
            PerishableCount = perishable,
        };
    }

    /// <summary>
    /// The song a file belongs to: its ledger identity, else its upload, else its name with the
    /// preset stripped for a file carrying no provenance.
    /// </summary>
    private static string TrackOf(string file, KeyLedger ledger) =>
        StemTagReader.Read(file)?.Provenance is { SourceVideoId: { } videoId }
            ? ledger.SongFor(videoId) ?? videoId
            : $"name:{StemFileName.WithoutPreset(file)}";

    private static string? MostCommon(
        IEnumerable<AbletonFileTags?> samples,
        Func<AbletonFileTags?, string?> value
    ) =>
        samples
            .Select(value)
            .OfType<string>()
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()
            ?.Key;
}

/// <summary>What the library holds, by key and genre, at one moment.</summary>
public sealed record KeyDistribution
{
    public required string Root { get; init; }

    /// <summary>All 24 keys, always — a key with no tracks is worth showing as a zero.</summary>
    public required IReadOnlyList<KeyCount> Counts { get; init; }

    /// <summary>Every genre on the list, a zero included, from the same scan (ADR 0030).</summary>
    public IReadOnlyList<GenreCount> Genres { get; init; } = [];

    public required int FileCount { get; init; }

    public int TrackCount { get; init; }

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

    public int KeyedTrackCount => Counts.Sum(c => c.Tracks);

    /// <summary>Tracks with no genre yet — not looked at, which is not the same as having none.</summary>
    public int UngenredCount => TrackCount - Genres.Sum(g => g.Tracks);

    /// <summary>The largest bucket, for scaling a bar against.</summary>
    public int Largest => Counts.Count is 0 ? 0 : Counts.Max(c => c.Tracks);

    public static KeyDistribution Empty(string root) =>
        new()
        {
            Root = root,
            Counts = [],
            FileCount = 0,
            Unkeyed = [],
        };
}

public sealed record KeyCount(MusicKey Key, int Tracks);

public sealed record GenreCount(string Genre, int Tracks);
