using System.Text.Json.Serialization;

namespace Crateside.Core.Imports;

/// <summary>What a track's source says about the release it came from, kept whatever the source (ADR 0077).</summary>
public sealed record Release
{
    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    public int? Track { get; init; }

    public int? TrackCount { get; init; }

    public int? Disc { get; init; }

    public int? DiscCount { get; init; }

    public string? Genre { get; init; }

    public string? Composer { get; init; }

    public string? Organization { get; init; }

    /// <summary>The catalogue number of the official soundtrack a gamerip carries.</summary>
    public string? Ost { get; init; }

    public int? Year { get; init; }

    /// <summary>The date as the file or site wrote it.</summary>
    public string? YearText { get; init; }

    public YearKind? YearKind { get; init; }

    /// <summary>The cover's file name in the covers folder.</summary>
    public string? Cover { get; init; }

    /// <summary>The release in short phrases for a header line: album, album artist, position, year.</summary>
    public IReadOnlyList<string> Describe() =>
        [
            .. new[]
            {
                Album,
                AlbumArtist is { Length: > 0 } artist && artist != Album ? artist : null,
                Track is { } track
                    ? TrackCount is { } count
                        ? $"track {track} of {count}"
                        : $"track {track}"
                    : null,
                Disc is { } disc && DiscCount is > 1 ? $"disc {disc} of {DiscCount}" : null,
                YearKind switch
                {
                    Imports.YearKind.Release when Year is { } year => $"{year}",
                    Imports.YearKind.Upload when Year is { } year => $"uploaded {year}",
                    _ => null,
                },
            }.OfType<string>(),
        ];

    [JsonIgnore]
    public int? ReleaseYear => YearKind is Imports.YearKind.Release ? Year : null;
}

[JsonConverter(typeof(JsonStringEnumConverter<YearKind>))]
public enum YearKind
{
    /// <summary>The year the recording came out.</summary>
    Release,

    /// <summary>The day it was uploaded, which says nothing about when it was made.</summary>
    Upload,
}
