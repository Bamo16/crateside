using System.Net;
using Crateside.Tests.Art;

namespace Crateside.Tests.Api;

/// <summary>What <c>/art/{trackKey}</c> serves, over synthetic state.</summary>
public sealed class CoverArtEndpointTests : IAsyncLifetime
{
    private SyntheticCrateside _crateside = null!;

    public async ValueTask InitializeAsync() => _crateside = await SyntheticCrateside.StartAsync();

    public ValueTask DisposeAsync() => _crateside.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_track_whose_file_carries_a_picture_gets_it_with_long_cache_headers()
    {
        Assert.SkipUnless(FfmpegAudio.IsAvailable, "No ffmpeg on PATH.");
        await FfmpegAudio.MakeAsync(_crateside.Instrumental, 320, 180);

        var small = await _crateside.Client.GetAsync($"/art/{SyntheticCrateside.Judged}", Ct);
        var full = await _crateside.Client.GetAsync(
            $"/art/{SyntheticCrateside.Judged}?size=full",
            Ct
        );

        Assert.Equal("image/jpeg", small.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=604800", small.Headers.CacheControl?.ToString());
        Assert.NotNull(small.Headers.ETag);
        Assert.True((await full.Content.ReadAsByteArrayAsync(Ct)).Length > 0);
        Assert.NotEqual(small.Headers.ETag, full.Headers.ETag);
    }

    [Fact]
    public async Task A_repeated_request_with_the_tag_is_answered_not_modified()
    {
        Assert.SkipUnless(FfmpegAudio.IsAvailable, "No ffmpeg on PATH.");
        await FfmpegAudio.MakeAsync(_crateside.Instrumental, 200, 200);
        var url = $"/art/{SyntheticCrateside.Judged}";
        var first = await _crateside.Client.GetAsync(url, Ct);

        using var again = new HttpRequestMessage(HttpMethod.Get, url);
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        Assert.Equal(
            HttpStatusCode.NotModified,
            (await _crateside.Client.SendAsync(again, Ct)).StatusCode
        );
    }

    [Theory]
    [InlineData(SyntheticCrateside.Waiting)]
    [InlineData("a-key-nothing-holds")]
    public async Task A_track_with_no_picture_gets_the_placeholder_rather_than_a_404(
        string trackKey
    )
    {
        var response = await _crateside.Client.GetAsync($"/art/{trackKey}?size=full", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_file_that_is_not_audio_gets_the_placeholder()
    {
        var response = await _crateside.Client.GetAsync($"/art/{SyntheticCrateside.Judged}", Ct);

        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
    }
}
