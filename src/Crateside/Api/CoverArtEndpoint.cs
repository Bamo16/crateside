using Crateside.Core.Art;
using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;
using Microsoft.Net.Http.Headers;

namespace Crateside.Api;

/// <summary>Serves a track's cover at <c>/art/{trackKey}?size=small|full</c> (#159); not part of the JSON API.</summary>
public static class CoverArtEndpoint
{
    private const string Placeholder =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 160"><rect width="160" height="160" fill="#808080" fill-opacity=".18"/></svg>""";

    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapCoverArt(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/art/{trackKey}", ServeAsync).ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> ServeAsync(
        string trackKey,
        string? size,
        TracksService tracks,
        SourceIndex sources,
        CoverArt art,
        HttpContext http,
        CancellationToken ct
    )
    {
        await tracks.EnsureFreshAsync(Freshness, ct);

        string[] samples = tracks.Current?.Rows.FirstOrDefault(row => row.TrackKey == trackKey)
            is { } row
            ? [.. row.Samples.Select(sample => sample.Path)]
            : [];
        var audio = samples.Concat(sources.PathOf(trackKey) is { } source ? [source] : []);

        if (
            await art.FindAsync(
                trackKey,
                audio,
                size is "full" ? CoverSize.Full : CoverSize.Small,
                ct
            )
            is not { } image
        )
        {
            http.Response.Headers.CacheControl = "public, max-age=60";

            return Results.Content(Placeholder, "image/svg+xml");
        }

        http.Response.Headers.CacheControl = "public, max-age=604800";

        return Results.File(
            image.Path,
            image.ContentType,
            entityTag: new EntityTagHeaderValue($"\"{image.ETag}\"")
        );
    }
}
