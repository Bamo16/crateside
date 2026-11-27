using System.Net;
using System.Net.Http.Json;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Api;

public sealed class TrackMarkApiTests : IAsyncLifetime
{
    private SyntheticCrateside _crateside = null!;

    public async ValueTask InitializeAsync() => _crateside = await SyntheticCrateside.StartAsync();

    public ValueTask DisposeAsync() => _crateside.DisposeAsync();

    [Fact]
    public async Task A_mark_is_set_under_who_asked_and_recorded_in_the_song_s_activity()
    {
        using var response = await SendAsync(
            HttpMethod.Put,
            SyntheticCrateside.Judged,
            "changesKey",
            new { requestedBy = "agent: coordination" }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [TrackMark.ChangesKey, TrackMark.OddBars],
            _crateside.Marks.Of(SyntheticCrateside.Song)
        );

        var action = Assert.Single(_crateside.Actions.For(SyntheticCrateside.Judged));

        Assert.Equal(UserActionKind.MarkSet, action.Kind);
        Assert.Equal("Changes key", action.Detail);
        Assert.Equal("agent: coordination", action.By);
    }

    [Fact]
    public async Task A_mark_is_cleared_and_asking_again_changes_and_records_nothing()
    {
        for (var i = 0; i < 2; i++)
        {
            using var response = await SendAsync(
                HttpMethod.Delete,
                SyntheticCrateside.Judged,
                "OddBars",
                new { requestedBy = "maintainer" }
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Empty(_crateside.Marks.Of(SyntheticCrateside.Song));

        var action = Assert.Single(_crateside.Actions.For(SyntheticCrateside.Judged));

        Assert.Equal(UserActionKind.MarkCleared, action.Kind);
        Assert.Equal("maintainer", action.By);
    }

    [Fact]
    public async Task A_request_that_names_nobody_or_an_unknown_mark_is_a_400_and_changes_nothing()
    {
        using var nobody = await SendAsync(
            HttpMethod.Put,
            SyntheticCrateside.Judged,
            "ChangesKey",
            new { requestedBy = " " }
        );
        using var unknown = await SendAsync(
            HttpMethod.Put,
            SyntheticCrateside.Judged,
            "Spicy",
            new { requestedBy = "maintainer" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, nobody.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal([TrackMark.OddBars], _crateside.Marks.Of(SyntheticCrateside.Song));
        Assert.Empty(_crateside.Actions.All());
    }

    [Fact]
    public async Task An_unknown_track_is_a_404()
    {
        using var response = await SendAsync(
            HttpMethod.Put,
            "NoSuchTrack1",
            "ChangesKey",
            new { requestedBy = "maintainer" }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string trackKey,
        string mark,
        object body
    ) =>
        _crateside.Client.SendAsync(
            new HttpRequestMessage(method, $"/api/tracks/{trackKey}/marks/{mark}")
            {
                Content = JsonContent.Create(body),
            },
            TestContext.Current.CancellationToken
        );
}
