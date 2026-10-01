using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class ActionLogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public ActionLogTests() => Directory.CreateDirectory(_root);

    private string LogPath => Path.Combine(_root, "actions.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_action_survives_a_restart_with_its_detail_and_the_pass_it_started()
    {
        new ActionLog(LogPath).Record(
            "aaaaaaaaaaa",
            UserActionKind.Recut,
            "Cut 2 more files",
            JobStage.Separation
        );

        var action = Assert.Single(new ActionLog(LogPath).All());
        Assert.Equal("aaaaaaaaaaa", action.VideoId);
        Assert.Equal(UserActionKind.Recut, action.Kind);
        Assert.Equal("Cut 2 more files", action.Detail);
        Assert.Equal(JobStage.Separation, action.PassFrom);
    }

    [Fact]
    public void A_track_reads_only_its_own_actions_oldest_first()
    {
        var log = new ActionLog(LogPath);

        log.Record("aaaaaaaaaaa", UserActionKind.Renamed, "First");
        log.Record("bbbbbbbbbbb", UserActionKind.Bumped);
        log.Record("aaaaaaaaaaa", UserActionKind.GenreSettled, "Drum & Bass");

        Assert.Equal(
            [UserActionKind.Renamed, UserActionKind.GenreSettled],
            log.For("aaaaaaaaaaa").Select(action => action.Kind)
        );
    }

    [Fact]
    public void An_action_with_no_pass_writes_none_and_a_torn_line_is_skipped()
    {
        new ActionLog(LogPath).Record("aaaaaaaaaaa", UserActionKind.Bumped);
        File.AppendAllText(LogPath, "{\"at\":" + Environment.NewLine);

        Assert.DoesNotContain("passFrom", File.ReadAllText(LogPath));
        Assert.Null(Assert.Single(new ActionLog(LogPath).All()).PassFrom);
    }
}
