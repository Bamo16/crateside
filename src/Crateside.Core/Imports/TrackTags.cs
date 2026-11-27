using System.Text.RegularExpressions;
using TagLib;
using TagLib.Ogg;

namespace Crateside.Core.Imports;

/// <summary>What an audio file's own tags say about its track.</summary>
public sealed partial record TrackTags
{
    public IReadOnlyList<string> Artists { get; init; } = [];

    public string? Title { get; init; }

    public Release Release { get; init; } = new();

    public byte[]? CoverBytes { get; init; }

    public string? CoverMimeType { get; init; }

    /// <summary>Null when the file cannot be read as audio.</summary>
    public static TrackTags? TryRead(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var xiph = file.GetTag(TagTypes.Xiph) as XiphComment;
            var id3 = file.GetTag(TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            var dateText =
                Text(xiph?.GetFirstField("DATE")) ?? (tag.Year > 0 ? $"{tag.Year}" : null);
            var picture = tag.Pictures is { Length: > 0 } pictures
                ? pictures.FirstOrDefault(
                    pic => pic is { Type: PictureType.FrontCover },
                    pictures[0]
                )
                : null;

            return new TrackTags
            {
                Artists = [.. tag.Performers.Where(artist => artist is { Length: > 0 })],
                Title = Text(tag.Title),
                CoverBytes = picture?.Data.Data,
                CoverMimeType = picture?.MimeType,
                Release = new Release
                {
                    Album = Text(tag.Album),
                    AlbumArtist = Text(string.Join(", ", tag.AlbumArtists)),
                    Track = Number(tag.Track),
                    TrackCount = Number(tag.TrackCount),
                    Disc = Number(tag.Disc),
                    DiscCount = Number(tag.DiscCount),
                    Genre = Text(string.Join(", ", tag.Genres)),
                    Composer = Text(string.Join(", ", tag.Composers)),
                    Organization =
                        Text(xiph?.GetFirstField("ORGANIZATION"))
                        ?? Text(id3?.GetTextAsString("TPUB")),
                    Ost = Text(xiph?.GetFirstField("OST")),
                    Year = FirstYear(dateText),
                    YearText = dateText,
                    YearKind = FirstYear(dateText) is null ? null : Imports.YearKind.Release,
                },
            };
        }
        catch (Exception ex)
            when (ex is CorruptFileException or UnsupportedFormatException or IOException)
        {
            return null;
        }
    }

    /// <summary><c>ARTIST - TITLE</c> from the tags; else the file name without its extension or leading track number.</summary>
    public static string NameFor(TrackTags? tags, string fileName) =>
        tags is { Title: { } title }
            ? tags.Artists is [_, ..] artists
                ? $"{string.Join(", ", artists)} - {title}"
                : title
            : WithoutTrackNumber(Path.GetFileNameWithoutExtension(fileName));

    /// <summary>A file name without its extension, minus a leading track number.</summary>
    public static string WithoutTrackNumber(string baseName) =>
        TrackNumberPrefix.Replace(baseName, string.Empty);

    /// <summary>The first four-digit year in a free-text date, or null.</summary>
    public static int? FirstYear(string? date) =>
        date is { Length: > 0 } && YearPattern.Match(date) is { Success: true, Value: var year }
            ? int.Parse(year, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static string? Text(string? value) =>
        value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    private static int? Number(uint value) => value > 0 ? (int)value : null;

    [GeneratedRegex(@"^\d+(-\d+)?\s*[.\-)]\s+")]
    private static partial Regex TrackNumberPrefix { get; }

    [GeneratedRegex(@"(?<!\d)\d{4}(?!\d)")]
    private static partial Regex YearPattern { get; }
}
