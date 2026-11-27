using Crateside.Core.Samples;

namespace Crateside.Core.Tracks;

/// <summary>The YouTube titles of a track's uploads, read from the tags of the files cut from each.</summary>
public static class UploadTitles
{
    /// <summary>The title of an upload, or null when no file of the track was cut from it or none carries one.</summary>
    public static string? Of(TrackRow row, string trackKey) =>
        row
            .Samples.Where(sample =>
                sample is { Path: { Length: > 0 } } && sample.SourceTrackKey == trackKey
            )
            .Select(sample => StemTagReader.Read(sample.Path)?.Title)
            .FirstOrDefault(title => title is not null);

    /// <summary>What names an upload: its title, else its video ID.</summary>
    public static string Name(TrackRow row, string trackKey) => Of(row, trackKey) ?? trackKey;
}
