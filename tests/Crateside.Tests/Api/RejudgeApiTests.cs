using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Api;

public sealed class RejudgeApiTests : IAsyncLifetime
{
    private SyntheticCrateside _crateside = null!;

    public async ValueTask InitializeAsync() => _crateside = await SyntheticCrateside.StartAsync();

    public ValueTask DisposeAsync() => _crateside.DisposeAsync();

    [Fact]
    public async Task A_plain_re_judge_is_queued_under_who_asked_and_shows_in_the_song_s_activity()
    {
        _crateside.Time.Advance(TimeSpan.FromHours(1));

        using var response = await PostAsync(
            SyntheticCrateside.Judged,
            new { requestedBy = "agent: coordination", reason = "judged on the wrong stretches" }
        );
        var queued = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Queued", (string?)queued["mode"]);
        Assert.Equal(SyntheticCrateside.Song, (string?)queued["song"]);
        Assert.Equal(1, (int?)queued["position"]);
        Assert.Equal("agent: coordination", (string?)queued["requestedBy"]);

        var entry = Assert.Single(_crateside.Rejudges.Read());

        Assert.Equal(SyntheticCrateside.Song, entry.Song);
        Assert.Equal("judged on the wrong stretches", entry.Reason);
        Assert.Equal("agent: coordination", entry.RequestedBy);
        Assert.Empty(_crateside.BarRuns.Started);

        var action = Assert.Single(_crateside.Actions.For(SyntheticCrateside.Judged));

        Assert.Equal(UserActionKind.Rejudged, action.Kind);
        Assert.Equal("agent: coordination", action.By);

        var shown = Assert.Single(
            TrackActivity.Build([], [], _crateside.Actions.For(SyntheticCrateside.Judged))
        );

        Assert.Equal("agent: coordination", shown.Label);
        Assert.Equal("rejudged: judged on the wrong stretches", shown.Text);
    }

    [Fact]
    public async Task A_request_that_names_nobody_is_a_400_and_queues_nothing()
    {
        foreach (var body in new object[] { new { reason = "x" }, new { requestedBy = "  " } })
        {
            using var response = await PostAsync(SyntheticCrateside.Judged, body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(_crateside.Rejudges.Read());
        Assert.Empty(_crateside.Actions.All());
    }

    [Fact]
    public async Task Bar_windows_are_read_through_the_warp_and_start_a_run_now()
    {
        using var response = await PostAsync(
            SyntheticCrateside.Judged,
            new
            {
                requestedBy = "maintainer",
                reason = "the chorus",
                windows = new object[]
                {
                    new
                    {
                        start = "1",
                        end = "2",
                        label = "verse",
                    },
                },
            }
        );
        var started = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Started", (string?)started["mode"]);
        Assert.Equal("1–2", (string?)started["windows"]![0]!["bars"]);

        var (order, direction) = Assert.Single(_crateside.BarRuns.Started);
        var window = Assert.Single(direction.Windows);

        Assert.Equal(SyntheticCrateside.Judged, order.TrackKey);
        Assert.Equal((0.5, 2.5, "verse"), (window.Start, window.End, window.Label));
        Assert.Equal("audio.flac", direction.Audio);
        Assert.Empty(_crateside.Rejudges.Read());
        Assert.Equal("maintainer", Assert.Single(_crateside.Actions.All()).By);
    }

    [Fact]
    public async Task Second_windows_are_taken_as_given_and_clamped_to_the_track()
    {
        using var response = await PostAsync(
            SyntheticCrateside.Judged,
            new
            {
                requestedBy = "maintainer",
                windows = new object[] { new { start = 1.5, end = 400 } },
            }
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var window = Assert.Single(Assert.Single(_crateside.BarRuns.Started).Direction.Windows);

        Assert.Equal(1.5, window.Start);
        Assert.True(window.End < 400);
    }

    [Fact]
    public async Task Windows_that_do_not_read_are_refused_and_start_nothing()
    {
        foreach (
            var window in new object[]
            {
                new { start = "nonsense", end = "2" },
                new { start = "1", end = 4.0 },
                new { start = 9.0, end = 3.0 },
            }
        )
        {
            using var response = await PostAsync(
                SyntheticCrateside.Judged,
                new { requestedBy = "maintainer", windows = new[] { window } }
            );

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(_crateside.BarRuns.Started);
        Assert.Empty(_crateside.Actions.All());
    }

    [Fact]
    public async Task A_run_already_going_for_the_track_is_a_409()
    {
        _crateside.BarRuns.Busy = true;

        using var response = await PostAsync(
            SyntheticCrateside.Judged,
            new
            {
                requestedBy = "maintainer",
                windows = new object[] { new { start = 1.0, end = 3.0 } },
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_crateside.Actions.All());
    }

    [Fact]
    public async Task A_track_never_judged_has_nothing_to_re_judge_and_an_unknown_one_is_a_404()
    {
        using var waiting = await PostAsync(
            SyntheticCrateside.Waiting,
            new { requestedBy = "maintainer" }
        );
        using var unknown = await PostAsync("Zx3Unknown99", new { requestedBy = "maintainer" });

        Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(_crateside.Rejudges.Read());
    }

    [Fact]
    public async Task An_explicit_re_judge_of_a_ruled_verdict_goes_through_and_is_owed()
    {
        Supersede(DecidedBy.Rules);

        using var response = await PostAsync(
            SyntheticCrateside.Judged,
            new { requestedBy = "agent: coordination" }
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(SyntheticCrateside.Song, Assert.Single(_crateside.Rejudges.Read()).Song);
        Assert.Equal(
            SyntheticCrateside.Song,
            Assert.Single(_crateside.Rejudges.Pending(_crateside.Ledger.Read())).Song
        );
    }

    [Fact]
    public async Task A_re_judge_of_a_persons_verdict_is_a_409_and_queues_nothing()
    {
        Supersede(DecidedBy.Person);

        using var response = await PostAsync(
            SyntheticCrateside.Judged,
            new { requestedBy = "agent: coordination" }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_crateside.Rejudges.Read());
        Assert.Empty(_crateside.Actions.All());
    }

    private void Supersede(DecidedBy by)
    {
        _crateside.Time.Advance(TimeSpan.FromHours(1));

        Assert.IsType<RecordOutcome.Recorded>(
            _crateside.Ledger.Record(
                new RunRecord
                {
                    RecordedAt = _crateside.Time.GetUtcNow(),
                    Song = SyntheticCrateside.Song,
                    Artist = "The Examples",
                    Title = "Synthetic Song",
                    SourceUrl = $"https://music.youtube.com/watch?v={SyntheticCrateside.Judged}",
                    Confidence = KeyConfidence.High,
                    DecidedBy = by,
                    Center = new TonalCenter("F#", DiatonicMode.Aeolian),
                    Sources =
                    [
                        new RunSource
                        {
                            Via = "test",
                            Kind = SourceKind.Ear,
                            States = "F# minor",
                            StatesKey = "F# minor",
                        },
                    ],
                }
            )
        );

        _crateside.Time.Advance(TimeSpan.FromHours(1));
    }

    private Task<HttpResponseMessage> PostAsync(string trackKey, object body) =>
        _crateside.Client.PostAsJsonAsync(
            $"/api/tracks/{trackKey}/rejudge",
            body,
            TestContext.Current.CancellationToken
        );

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!;
}
