using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Pairing;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Library;

/// <summary>
/// The finished library one song at a time, keys and genres from Ableton's index (ADR 0013): a
/// song's samples and sources count once between them.
/// </summary>
public sealed class LibraryKeyReader(
    LedgerStore store,
    IOptions<CratesidePaths> paths,
    FileIndexReader index,
    FolderInfoReader folderInfo,
    WarpTempoReader tempos,
    BeatReader? beats = null
)
{
    private readonly CratesidePaths _paths = paths.Value;

    public KeyDistribution Read()
    {
        var root = _paths.LibraryFolder;

        if (!Directory.Exists(root))
            return KeyDistribution.Empty(root);

        var tags = AbletonReads.TagsOrEmpty(index.Read, root);
        var backed = AbletonReads.TagsOrEmpty(folderInfo.Read, root);
        var ledger = store.Read();
        var tracks = new Dictionary<string, List<(string File, FileTags? Tags)>>(
            StringComparer.OrdinalIgnoreCase
        );
        var trackKeys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var unkeyed = new List<string>();
        var files = 0;
        var perishable = 0;

        foreach (
            var file in Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(SampleFiles.IsAudio)
        )
        {
            files++;

            var applied = tags.TryGet(file, out var found) ? found : null;

            var (track, trackKey) = TrackOf(file, ledger);

            if (!tracks.TryGetValue(track, out var samples))
                tracks[track] = samples = [];

            samples.Add((file, applied));
            trackKeys[file] = trackKey;

            if (MusicKey.FromString(applied?.Key) is null)
                unkeyed.Add(Path.GetRelativePath(root, file));
            else if (!backed.TryGet(file, out _))
                perishable++;
        }

        var songs = tracks
            .Select(pair => Song(pair.Key, pair.Value, trackKeys, ledger))
            .OrderBy(song => song.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new KeyDistribution
        {
            Root = root,
            Counts =
            [
                .. MusicKey.AllKeys.Select(k => new KeyCount(
                    k,
                    songs.Count(song => k.Equals(song.Key))
                )),
            ],
            Genres =
            [
                .. GenreVocabulary.All.Select(g => new GenreCount(
                    g,
                    songs.Count(song => song.Genres.Contains(g, StringComparer.OrdinalIgnoreCase))
                )),
            ],
            FileCount = files,
            TrackCount = tracks.Count,
            Unkeyed = [.. unkeyed.Order(StringComparer.OrdinalIgnoreCase)],
            PerishableCount = perishable,
            Songs = songs,
        };
    }

    private LibrarySong Song(
        string track,
        List<(string File, FileTags? Tags)> samples,
        Dictionary<string, string?> trackKeys,
        KeyLedger ledger
    )
    {
        var run = ledger.TryGetLatestBySong(track, out var found) ? found : null;
        var name = run is { Artist.Length: > 0, Title.Length: > 0 }
            ? run.DisplayName
            : StemFileName.WithoutPreset(samples[0].File);

        var instrumentals = samples.Where(s => BeatReader.IsInstrumental(s.File, s.Tags)).ToList();
        var tempo = samples
            .OrderByDescending(s => instrumentals.Contains(s))
            .Select(s => tempos.Read(s.File))
            .FirstOrDefault(read => read is not null);
        var reading = instrumentals
            .Select(s => beats?.ReadFor(trackKeys[s.File], s.File))
            .FirstOrDefault(read => read is not null);

        return new LibrarySong
        {
            Song = track,
            Name = name,
            Artist = name.Split(" - ", 2) is [{ Length: > 0 } credited, _]
                ? ArtistCredit.Primary(credited)
                : null,
            Key = MusicKey.FromString(MostCommon(samples, s => s.Tags?.Key)),
            Genres =
            [
                .. samples
                    .SelectMany(s => s.Tags?.Genres ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ],
            Tempo = tempo,
            Beats = reading,
            Starter = StarterFates.Of(reading, tempo),
            Run = run,
        };
    }

    /// <summary>
    /// The song a file belongs to: its ledger identity, else its upload, else its name with the
    /// preset stripped for a file carrying no provenance.
    /// </summary>
    private static (string Track, string? TrackKey) TrackOf(string file, KeyLedger ledger) =>
        StemTagReader.Read(file)?.Provenance is { SourceTrackKey: { } trackKey }
            ? (ledger.SongFor(trackKey) ?? trackKey, trackKey)
            : (SourcelessSongs.KeyOf(file), null);

    private static string? MostCommon(
        IEnumerable<(string File, FileTags? Tags)> samples,
        Func<(string File, FileTags? Tags), string?> value
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

    /// <summary>Each of Ableton's 29 genres, a zero included, from the same scan (ADR 0088); a song with two counts under both.</summary>
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

    public IReadOnlyList<LibrarySong> Songs { get; init; } = [];

    public int KeyedTrackCount => Counts.Sum(c => c.Tracks);

    /// <summary>Tracks carrying none of Ableton's genres — not looked at, which is not the same as having none.</summary>
    public int UngenredCount => Songs.Count(song => !song.Genres.Any(GenreVocabulary.IsGenre));

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
