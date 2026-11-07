using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class BarJudgingTests
{
    [Fact]
    public void A_file_keyed_track_judges_under_its_track_key_not_a_watch_url()
    {
        var order = BarJudging.OrderFor(
            new TrackRow
            {
                TrackKey = "file~25edb68200c05e82",
                Title = "Dave Wise - Lockjaw's Saga",
            }
        );

        Assert.Equal("track:file~25edb68200c05e82", order.SourceUrl);
    }

    [Fact]
    public void A_youtube_track_judges_under_its_watch_url()
    {
        var order = BarJudging.OrderFor(
            new TrackRow
            {
                TrackKey = "dQw4w9WgXcQ",
                Title = "Rick Astley - Never Gonna Give You Up",
            }
        );

        Assert.Equal("https://music.youtube.com/watch?v=dQw4w9WgXcQ", order.SourceUrl);
    }
}
