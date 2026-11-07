using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Tracks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

namespace Crateside.Api;

/// <summary>The read-only JSON API under <c>/api</c>: what the pages read, serialized as they read it (#167).</summary>
public static class CratesideApi
{
    /// <summary>How old the rows may be before a request rebuilds them first, as the pages allow.</summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddCratesideApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.IncludeFields = true;
        });
        services.AddOpenApi(options =>
            options.AddDocumentTransformer(
                (document, _, _) =>
                {
                    document.Info.Title = "crateside";
                    document.Info.Description =
                        "Read-only JSON over what crateside's pages show, on localhost only. See docs/api.md.";

                    return Task.CompletedTask;
                }
            )
        );

        return services;
    }

    /// <summary>The endpoints, the OpenAPI document at <c>/openapi/v1.json</c>, and Scalar at <c>/scalar</c>.</summary>
    public static IEndpointRouteBuilder MapCratesideApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
        endpoints.MapScalarApiReference(options => options.WithTitle("crateside API"));

        var tracks = endpoints.MapGroup("/api/tracks").WithTags("Tracks");

        tracks
            .MapGet(string.Empty, ListAsync)
            .WithName("ListTracks")
            .WithSummary("The Tracks page's rows, narrowed as its filters narrow them.");
        tracks
            .MapGet("/{trackKey}", ReadAsync)
            .WithName("GetTrack")
            .WithSummary(
                "One track: its row, verdict evidence, job history, stems with their warps, and its store records."
            );

        return endpoints;
    }

    private static async Task<Results<Ok<TracksView>, ProblemHttpResult>> ListAsync(
        TracksService tracks,
        [Description("The Tracks page's view: Unfinished when not given.")]
            TrackFilter filter = TrackFilter.Unfinished,
        [Description(
            "Matches a name, key or video ID across every track, ignoring the filter, as the page's search does."
        )]
            string? search = null,
        [Description("Only rows tagged with this familiarity.")] string? familiarity = null,
        CancellationToken ct = default
    )
    {
        await tracks.EnsureFreshAsync(Freshness, ct);

        return tracks.Current is { } view
            ? TypedResults.Ok(
                view with
                {
                    Rows = [.. view.Rows.Narrow(filter, search, familiarity)],
                }
            )
            : TypedResults.Problem(
                tracks.LastError,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The rows have not been read yet"
            );
    }

    /// <summary>A missing track's 404 carries a body, so the status code pages leave it alone.</summary>
    private static async Task<Results<Ok<TrackDetails>, NotFound<ProblemDetails>>> ReadAsync(
        [Description(
            "The track key, as in /song/{trackKey}: a YouTube video ID, or what ADR 0077 keys any other source by."
        )]
            string trackKey,
        TracksService tracks,
        TrackDetailsReader details,
        CancellationToken ct
    )
    {
        await tracks.EnsureFreshAsync(Freshness, ct);

        return details.Read(trackKey) is { } found
            ? TypedResults.Ok(found)
            : TypedResults.NotFound(
                new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = $"crateside has no track for {trackKey}",
                }
            );
    }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    IncludeFields = true
)]
[JsonSerializable(typeof(TracksView))]
[JsonSerializable(typeof(TrackDetails))]
internal sealed partial class ApiJsonContext : JsonSerializerContext { }
