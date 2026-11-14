using Crateside.Core.Beats;
using Crateside.Core.Library;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Library;

/// <summary>The staged songs Tools' search offers beside the library's (#155).</summary>
public sealed class StagedSongsTests
{
    private static readonly TrackRow Base = new()
    {
        TrackKey = "aaaaaaaaaaa",
        Title = "GYARI - Akari Has Arrived",
        Samples = [new TrackSample { Label = "Vocal - Full", AbletonKey = "A minor" }],
    };

    [Fact]
    public void A_track_whose_stems_are_in_staging_is_offered_and_marked_staged()
    {
        var song = Assert.Single(StagedSongs.Of([Base], []));

        Assert.True(song.IsStaged);
        Assert.Equal("GYARI - Akari Has Arrived", song.Name);
        Assert.Equal("GYARI", song.Artist);
        Assert.Equal(Base.Song, song.Song);
    }

    [Fact]
    public void A_staged_track_with_no_warp_shows_the_tempo_crateside_read()
    {
        var reading = new BeatReading { Bpm = 128 };

        var song = Assert.Single(
            StagedSongs.Of(
                [
                    Base with
                    {
                        Samples =
                        [
                            new TrackSample
                            {
                                Label = "Vocal - Full",
                                AbletonKey = "A minor",
                                Beats = reading,
                            },
                        ],
                    },
                ],
                []
            )
        );

        Assert.Null(song.Tempo);
        Assert.Equal(128, song.Bpm?.Value);
    }

    [Theory]
    [InlineData(JobStage.Evidence)]
    [InlineData(JobStage.Separation)]
    [InlineData(JobStage.Handoff)]
    public void A_track_still_in_flight_is_not_offered(JobStage stage)
    {
        Assert.Empty(StagedSongs.Of([Base with { RunningStage = stage }], []));
    }

    [Fact]
    public void A_song_the_library_already_holds_is_offered_from_the_library_alone()
    {
        var held = new LibrarySong { Song = Base.Song, Name = "GYARI - Akari Has Arrived" };

        Assert.Empty(StagedSongs.Of([Base], [held]));
    }

    [Fact]
    public void A_track_with_all_its_samples_in_the_library_is_not_staged()
    {
        var done = Base with
        {
            Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
        };

        Assert.Empty(StagedSongs.Of([done], []));
    }
}
