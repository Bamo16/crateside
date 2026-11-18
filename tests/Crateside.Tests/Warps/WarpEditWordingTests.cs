using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class WarpEditWordingTests
{
    [Fact]
    public void Changes_names_each_edit_a_draft_holds()
    {
        var draft = new WarpDraft().Later(2).Nudged(4);

        var words = WarpEditWording.Changes(draft);

        Assert.Contains("bar two beats later", words);
        Assert.Contains("+4 ms", words);
    }

    [Fact]
    public void Saves_to_lists_what_is_written_kept_and_lent()
    {
        var lines = WarpEditWording.SavesTo(["Drums", "Vocal"], ["Bass"], ["Drums - Kick"]);

        Assert.Equal(
            [
                "Saves to Drums and Vocal",
                "Bass keeps its own warp",
                "then lends it again to Drums - Kick",
            ],
            lines
        );
    }

    [Fact]
    public void Nudge_reads_as_on_disk_until_it_is_moved()
    {
        Assert.False(WarpEditWording.Nudge(new WarpDraft(), starter: true).Changed);
        Assert.Equal(
            "+3 ms from where crateside put it",
            WarpEditWording.Nudge(new WarpDraft().Nudged(3), starter: true).Text
        );
    }
}
