using System.Net;
using System.Text.Json.Nodes;

namespace Crateside.Tests.Api;

/// <summary>What the API answers about tracks, read off synthetic state the way the pages read it.</summary>
public sealed class CratesideApiTests : IAsyncLifetime
{
    private SyntheticCrateside _crateside = null!;

    public async ValueTask InitializeAsync() => _crateside = await SyntheticCrateside.StartAsync();

    public ValueTask DisposeAsync() => _crateside.DisposeAsync();

    [Fact]
    public async Task A_genre_filed_under_the_video_ID_shows_under_that_key_beside_the_song_s_records()
    {
        var track = await GetAsync($"/api/tracks/{SyntheticCrateside.Judged}");
        var records = track["records"]!;

        Assert.Equal(
            ["synthwave"],
            records["genres"]![SyntheticCrateside.Judged]!["heard"]!
                .AsArray()
                .Select(heard => (string?)heard)
        );
        Assert.Null(records["genres"]![SyntheticCrateside.Song]);
        Assert.Equal(1_200, (long?)records["familiarity"]![SyntheticCrateside.Song]!["artistFans"]);
        Assert.Equal("OddBars", (string?)records["marks"]![SyntheticCrateside.Song]!["marks"]![0]);
        Assert.Empty(records["resolvedNames"]!.AsObject());
    }

    [Fact]
    public async Task The_track_carries_its_row_and_the_evidence_behind_its_verdict()
    {
        var track = await GetAsync($"/api/tracks/{SyntheticCrateside.Judged}");

        Assert.Equal(SyntheticCrateside.Song, (string?)track["row"]!["song"]);
        Assert.Equal("ReadyToMove", (string?)track["row"]!["state"]);
        Assert.Equal("cut from", (string?)track["row"]!["uploads"]![0]!["role"]);
        Assert.Equal("F# minor", (string?)track["evidence"]!["key"]);
        Assert.Equal("High", (string?)track["row"]!["verdict"]!["confidence"]);
    }

    [Fact]
    public async Task The_job_history_holds_the_track_s_lines_and_no_other_track_s()
    {
        var jobs = (await GetAsync($"/api/tracks/{SyntheticCrateside.Judged}"))["jobs"]!.AsArray();

        Assert.Equal(["Running", "Succeeded"], jobs.Select(job => (string?)job!["status"]));
        Assert.All(jobs, job => Assert.Equal(SyntheticCrateside.Judged, (string?)job!["videoId"]));
    }

    [Fact]
    public async Task A_stem_s_warp_comes_parsed_with_crateside_s_records_about_it()
    {
        var stem = (await GetAsync($"/api/tracks/{SyntheticCrateside.Judged}"))["stems"]![0]!;
        var warp = stem["warp"]!;

        Assert.Equal(_crateside.Instrumental, (string?)stem["path"]);
        Assert.Equal("starter", (string?)warp["owner"]!["kind"]);
        Assert.True((bool?)warp["isWarped"]);
        Assert.Equal(
            [(0.5, 0.0), (2.5, 4.0)],
            warp["markers"]!
                .AsArray()
                .Select(marker => ((double)marker!["seconds"]!, (double)marker["beat"]!))
        );
        Assert.Equal((0.5, 8.5), ((double)warp["loop"]!["start"]!, (double)warp["loop"]!["end"]!));
        Assert.Equal(120, (double?)warp["tempo"]!["bpm"]);
        Assert.True((bool?)warp["loop"]!["on"]);
        Assert.Null((string?)warp["written"]!["from"]);
        Assert.Equal(
            [
                Path.Combine(
                    _crateside.WarpBackups,
                    $"2026-10-01 115900 {Path.GetFileName(_crateside.Instrumental)}.asd"
                ),
            ],
            warp["backups"]!.AsArray().Select(backup => (string?)backup)
        );
    }

    [Fact]
    public async Task An_unknown_track_is_a_404_with_a_problem_body()
    {
        using var response = await _crateside.Client.GetAsync(
            "/api/tracks/Zx3Unknown99",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Zx3Unknown99", (string?)(await ReadAsync(response))["title"]);
    }

    [Fact]
    public async Task The_rows_are_narrowed_as_the_Tracks_page_narrows_them()
    {
        Assert.Equal(
            [SyntheticCrateside.Judged, SyntheticCrateside.Waiting],
            await VideoIdsAsync("/api/tracks")
        );
        Assert.Equal(
            [SyntheticCrateside.Judged, SyntheticCrateside.Waiting, SyntheticCrateside.Filed],
            await VideoIdsAsync("/api/tracks?filter=All")
        );
        Assert.Equal(
            [SyntheticCrateside.Judged],
            await VideoIdsAsync("/api/tracks?filter=InStaging")
        );
        Assert.Equal(
            [SyntheticCrateside.Filed],
            await VideoIdsAsync("/api/tracks?filter=InStaging&search=Filed")
        );
    }

    [Fact]
    public async Task A_filter_the_page_does_not_have_is_refused()
    {
        using var response = await _crateside.Client.GetAsync(
            "/api/tracks?filter=Everything",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string[]> VideoIdsAsync(string url) =>
        [
            .. (await GetAsync(url))["rows"]!
                .AsArray()
                .Select(row => row!["videoId"]!.GetValue<string>()),
        ];

    private async Task<JsonNode> GetAsync(string url)
    {
        using var response = await _crateside.Client.GetAsync(
            url,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadAsync(response);
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!;
}
