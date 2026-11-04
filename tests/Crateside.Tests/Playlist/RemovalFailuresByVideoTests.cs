using Crateside.Core.Playlist;

namespace Crateside.Tests.Playlist;

public sealed class RemovalFailuresByVideoTests
{
    [Fact]
    public void A_video_held_twice_whose_entries_both_fail_is_one_failure_not_a_crash()
    {
        var trackKeyOf = new Dictionary<string, string>
        {
            ["entry-1"] = "abc",
            ["entry-2"] = "abc",
        };
        RemovalResult[] results =
        [
            new()
            {
                PlaylistItemId = "entry-1",
                Title = "T",
                Removed = false,
                Reason = "forbidden",
            },
            new()
            {
                PlaylistItemId = "entry-2",
                Title = "T",
                Removed = false,
                Reason = "forbidden",
            },
        ];

        var failures = PlaylistConsumer.FailuresByVideo(results, trackKeyOf);

        Assert.Equal("forbidden", Assert.Single(failures).Value.Reason);
    }

    [Fact]
    public void An_entry_that_was_removed_is_no_failure()
    {
        var trackKeyOf = new Dictionary<string, string> { ["entry-1"] = "abc" };
        RemovalResult[] results =
        [
            new()
            {
                PlaylistItemId = "entry-1",
                Title = "T",
                Removed = true,
            },
        ];

        Assert.Empty(PlaylistConsumer.FailuresByVideo(results, trackKeyOf));
    }
}
