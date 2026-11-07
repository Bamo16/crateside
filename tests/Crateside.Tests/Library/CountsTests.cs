using Crateside.Core.Ableton;
using Crateside.Core.Library;
using Crateside.Core.Pairing;

namespace Crateside.Tests.Library;

public sealed class CountsTests
{
    [Fact]
    public void Tempos_bin_by_ten_with_the_slow_end_and_a_sparse_top_merged()
    {
        double[] bpms = [55, 64, 72, 75, 79, 81, 85, 88, 128, 128, 128, 201, 214];

        var counts = TempoCounts.From(bpms.Select(Song));

        Assert.Equal(
            ["50–69", "70–79", "80–89", "90–99", "100–109", "110–119", "120–129", "130–219"],
            counts.Bins.Select(bin => bin.Label)
        );
        Assert.Equal(13, counts.Bins.Sum(bin => bin.Songs));
    }

    [Fact]
    public void A_tempo_ten_songs_share_is_called_out_in_its_bin()
    {
        var counts = TempoCounts.From([
            .. Enumerable.Repeat(174.0, 10).Select(Song),
            Song(172),
            Song(170),
            Song(175),
        ]);

        var bin = Assert.Single(counts.Bins, bin => bin.Low is 170);
        Assert.Equal([new TempoPeak(174, 10)], bin.Peaks);
        Assert.Equal(10, bin.PeakSongs);
    }

    [Fact]
    public void Only_artists_with_three_or_more_songs_are_listed()
    {
        var counts = ArtistCounts.From([
            Song(128, "Ludacris"),
            Song(95, "Ludacris"),
            Song(100, "ludacris"),
            Song(174, "S3RL"),
            Song(175, "S3RL"),
        ]);

        var artist = Assert.Single(counts.Artists);
        Assert.Equal(("Ludacris", 3, "95–128"), (artist.Artist, artist.Songs, artist.BpmRange));
        Assert.Equal(2, counts.AllArtists);
    }

    private static LibrarySong Song(double bpm) => Song(bpm, "Artist");

    private static LibrarySong Song(double bpm, string artist) =>
        new()
        {
            Song = $"{artist} - {bpm}",
            Name = $"{artist} - {bpm}",
            Artist = artist,
            Key = MusicKey.FromString("A minor"),
            Tempo = new WarpTempo(bpm, TempoShape.Steady, bpm, bpm, []),
        };
}
