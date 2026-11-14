using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Crateside.Tests.Api;

/// <summary>What <c>/audio/{trackKey}/{file}</c> serves, over synthetic state.</summary>
public sealed class AudioEndpointTests : IAsyncLifetime
{
    private const string StemName = "Synthetic Song (Instrumental - Full).flac";

    private SyntheticCrateside _crateside = null!;

    public async ValueTask InitializeAsync() => _crateside = await SyntheticCrateside.StartAsync();

    public ValueTask DisposeAsync() => _crateside.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> GetAsync(string trackKey, string file) =>
        _crateside.Client.GetAsync($"/audio/{trackKey}/{Uri.EscapeDataString(file)}", Ct);

    [Fact]
    public async Task A_stem_is_served_whole_as_audio_that_accepts_ranges()
    {
        var response = await GetAsync(SyntheticCrateside.Judged, StemName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/flac", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task A_range_request_gets_exactly_the_bytes_asked_for()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/audio/{SyntheticCrateside.Judged}/{Uri.EscapeDataString(StemName)}"
        );
        request.Headers.Range = new RangeHeaderValue(1, 2);

        var response = await _crateside.Client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 1-2/3", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal([2, 3], await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task The_mix_is_served_from_the_working_set_by_the_name_mix()
    {
        var folder = Path.Combine(_crateside.WorkingFolder, SyntheticCrateside.Judged);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "source.flac"), [9, 8, 7, 6], Ct);

        var response = await GetAsync(SyntheticCrateside.Judged, "mix");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([9, 8, 7, 6], await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task A_mix_that_is_gone_is_a_404_the_page_can_say_so_from()
    {
        var response = await GetAsync(SyntheticCrateside.Judged, "mix");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("moved or is gone", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_stem_that_moved_from_staging_to_the_library_is_found_where_it_is_now()
    {
        Directory.CreateDirectory(_crateside.Library);
        File.Move(_crateside.Instrumental, Path.Combine(_crateside.Library, StemName));

        var response = await GetAsync(SyntheticCrateside.Judged, StemName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task A_stem_whose_file_is_gone_from_every_folder_is_a_404()
    {
        File.Delete(_crateside.Instrumental);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await GetAsync(SyntheticCrateside.Judged, StemName)).StatusCode
        );
    }

    [Fact]
    public async Task A_name_the_track_does_not_have_is_not_said_to_have_moved()
    {
        var response = await GetAsync(SyntheticCrateside.Judged, "not-a-file.flac");

        Assert.DoesNotContain("moved or is gone", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData(
        SyntheticCrateside.Judged,
        "..\\Staging\\Synthetic Song (Instrumental - Full).flac"
    )]
    [InlineData(SyntheticCrateside.Judged, "not-a-file.flac")]
    [InlineData("a-key-nothing-holds", "mix")]
    [InlineData(SyntheticCrateside.Waiting, "mix")]
    public async Task A_name_that_is_not_one_of_the_track_files_is_a_404_never_a_path(
        string trackKey,
        string file
    ) => Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(trackKey, file)).StatusCode);

    [Fact]
    public async Task A_stems_loudness_is_measured_for_the_player_and_kept_for_the_next_ask()
    {
        var url = $"/audio/{SyntheticCrateside.Judged}/{Uri.EscapeDataString(StemName)}/loudness";

        var first = await _crateside.Client.GetFromJsonAsync<LoudnessReply>(url, Ct);
        var second = await _crateside.Client.GetFromJsonAsync<LoudnessReply>(url, Ct);

        Assert.Equal(-7.5, first?.Lufs);
        Assert.Equal(-7.5, second?.Lufs);
        Assert.Equal(1, _crateside.Ebur128.Runs);
    }

    [Fact]
    public async Task The_loudness_of_a_file_that_is_not_one_of_the_tracks_is_a_404()
    {
        var response = await _crateside.Client.GetAsync(
            $"/audio/{SyntheticCrateside.Judged}/mix/loudness",
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record LoudnessReply(double? Lufs);
}
