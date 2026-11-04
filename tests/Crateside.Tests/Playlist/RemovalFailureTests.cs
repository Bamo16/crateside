using System.Net;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Google;
using Google.Apis.Requests;

namespace Crateside.Tests.Playlist;

/// <summary>A playlist entry crateside could not remove says so, and whether a retry could help (#34).</summary>
public sealed class RemovalFailureTests : IDisposable
{
    private const string Track = "2E33rkabbF4";
    private const string Other = "p3NZn0mA_XI";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public RemovalFailureTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Why_an_entry_was_left_behind_survives_a_restart()
    {
        var store = Store();

        store.Record([Item(Track), Item(Other)]);
        store.MarkRemovalAttempted([Track, Other], DateTimeOffset.UtcNow);
        store.RecordRemovalFailures(
            new Dictionary<string, RemovalFailure>
            {
                [Track] = new()
                {
                    Reason = "forbidden: the authorized account cannot modify this playlist.",
                },
            }
        );

        var left = Assert.Single(Store().Read(), record => record.RemovalFailure is not null);

        Assert.Equal(Track, left.TrackKey);
        Assert.False(left.RemovalFailure!.CanSucceedLater);
    }

    [Fact]
    public void Trying_again_puts_the_entry_back_in_line_and_forgets_the_failure()
    {
        var store = Store();

        store.Record([Item(Track)]);
        store.MarkRemovalAttempted([Track], DateTimeOffset.UtcNow);
        store.RecordRemovalFailures(
            new Dictionary<string, RemovalFailure>
            {
                [Track] = new() { Reason = "quotaExceeded", CanSucceedLater = true },
            }
        );

        store.ClearRemovalAttempted([Track]);

        var record = Assert.Single(Store().Read());

        Assert.Null(record.RemovalAttemptedAt);
        Assert.Null(record.RemovalFailure);
    }

    [Fact]
    public void An_entry_gone_from_the_playlist_is_no_longer_left_but_is_not_offered_again()
    {
        var store = Store();

        store.Record([Item(Track)]);
        store.MarkRemovalAttempted([Track], DateTimeOffset.UtcNow);
        store.RecordRemovalFailures(
            new Dictionary<string, RemovalFailure>
            {
                [Track] = new()
                {
                    Reason = "SERVICE_UNAVAILABLE: The operation was aborted.",
                    CanSucceedLater = true,
                },
            }
        );

        store.ForgetRemovalFailures([Track]);

        var record = Assert.Single(Store().Read());

        Assert.Null(record.RemovalFailure);
        Assert.NotNull(record.RemovalAttemptedAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", false)]
    [InlineData(HttpStatusCode.Forbidden, "playlistItemsNotAccessible", false)]
    [InlineData(HttpStatusCode.Forbidden, "insufficientPermissions", false)]
    [InlineData(HttpStatusCode.Forbidden, "quotaExceeded", true)]
    [InlineData(HttpStatusCode.TooManyRequests, "rateLimitExceeded", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, true)]
    [InlineData(HttpStatusCode.Conflict, "SERVICE_UNAVAILABLE", true)]
    public void Only_a_failure_of_the_moment_is_worth_another_try(
        HttpStatusCode status,
        string? reason,
        bool canSucceedLater
    )
    {
        // A wrong-account 403 fails identically for ever, and each try spends fifty units.
        var error = new GoogleApiException("youtube", "refused")
        {
            HttpStatusCode = status,
            Error = new RequestError
            {
                Errors = reason is null ? [] : [new SingleError { Reason = reason }],
            },
        };

        Assert.Equal(canSucceedLater, PlaylistRemover.CanSucceedLater(error));
    }

    private IntakeStore Store() => new(Path.Combine(_directory, "intake.json"));

    private static WorkItem Item(string trackKey) =>
        new()
        {
            TrackKey = trackKey,
            Title = "Light Years Away",
            AddedAt = DateTimeOffset.Parse("2026-09-17T18:40:00+00:00"),
        };
}
