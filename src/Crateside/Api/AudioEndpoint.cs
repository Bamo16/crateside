using Crateside.Core.Loudness;
using Crateside.Core.Playback;
using Crateside.Core.Tracks;

namespace Crateside.Api;

/// <summary>Serves a track's audio to the player at <c>/audio/{trackKey}/{file}</c>, by the track and file it names and never a path (ADR 0094).</summary>
public static class AudioEndpoint
{
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapAudio(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/audio/{trackKey}/{file}", ServeAsync).ExcludeFromDescription();
        endpoints
            .MapGet("/audio/{trackKey}/{file}/loudness", LoudnessAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> ServeAsync(
        string trackKey,
        string file,
        TracksService tracks,
        PlayableFiles playable,
        HttpContext http,
        CancellationToken ct
    )
    {
        await tracks.EnsureFreshAsync(Freshness, ct);

        if (tracks.Current?.Rows.FirstOrDefault(row => row.TrackKey == trackKey) is not { } row)
            return Results.Problem(
                title: "That track is not known",
                detail: "Crateside has no such track yet; try again in a moment.",
                statusCode: StatusCodes.Status404NotFound
            );

        if (!PlayableFiles.Knows(row, file))
            return Results.Problem(
                title: "That file is not part of the track",
                detail: "The track has no such file to play.",
                statusCode: StatusCodes.Status404NotFound
            );

        if (playable.Find(row, file) is not { } found)
            return Results.Problem(
                title: "That file is not on disk",
                detail: "The file moved or is gone; try again in a moment.",
                statusCode: StatusCodes.Status404NotFound
            );

        // A stem is rewritten in place when it is re-cut, so a cached copy would play the old one.
        http.Response.Headers.CacheControl = "no-cache";

        return Results.File(
            found.Path,
            ContentTypeOf(found.Path),
            lastModified: File.GetLastWriteTimeUtc(found.Path),
            enableRangeProcessing: true
        );
    }

    /// <summary>The file's integrated loudness for the player to level the music by, measured now when it has no kept reading; null when ffmpeg finds none.</summary>
    private static async Task<IResult> LoudnessAsync(
        string trackKey,
        string file,
        TracksService tracks,
        PlayableFiles playable,
        LoudnessReader reader,
        CancellationToken ct
    )
    {
        await tracks.EnsureFreshAsync(Freshness, ct);

        if (
            tracks.Current?.Rows.FirstOrDefault(row => row.TrackKey == trackKey) is not { } row
            || playable.Find(row, file) is not { } found
        )
            return Results.NotFound();

        var reading = reader.Read(found.Path) ?? await reader.MeasureAsync(found.Path, ct);

        return Results.Json(new PlaybackLoudness(reading?.IntegratedLufs));
    }

    private static string ContentTypeOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" or ".aac" => "audio/mp4",
            ".ogg" or ".opus" => "audio/ogg",
            _ => "application/octet-stream",
        };
}

internal sealed record PlaybackLoudness(double? Lufs);
