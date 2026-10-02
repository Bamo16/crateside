using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Tracks;

/// <summary>What a row is called before StemForge names it, and what a lookup may take from that.</summary>
public sealed class StandInNameTests
{
    [Theory]
    [InlineData("Creep", "Radiohead - Topic", "Radiohead - Creep")]
    [InlineData("Creep", "Radiohead", "Radiohead - Creep")]
    [InlineData(
        "Radiohead - Creep (Official Video)",
        "RadioheadVEVO",
        "Radiohead - Creep (Official Video)"
    )]
    [InlineData("Creep", null, "Creep")]
    public void The_uploader_goes_before_a_title_that_names_no_artist(
        string title,
        string? channel,
        string expected
    ) =>
        Assert.Equal(
            expected,
            new WorkItem
            {
                VideoId = "zFYEYRcjK2g",
                Title = title,
                ChannelTitle = channel,
            }.StandInTitle
        );

    [Fact]
    public void A_row_named_only_by_its_upload_offers_no_artist_to_look_up()
    {
        var row = new TrackRow
        {
            VideoId = "zFYEYRcjK2g",
            Title = "Radiohead - Creep",
            NameSource = TrackNameSource.Playlist,
        };

        Assert.Null(row.ArtistAndTitle);
        Assert.Equal(
            ("Radiohead", "Creep"),
            (row with { NameSource = TrackNameSource.Resolved }).ArtistAndTitle
        );
    }
}
