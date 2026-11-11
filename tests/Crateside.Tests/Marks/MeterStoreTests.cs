using Crateside.Core.Ableton;
using Crateside.Core.Marks;

namespace Crateside.Tests.Marks;

public sealed class MeterStoreTests : IDisposable
{
    private const string Song = "Artist - Title";

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string FilePath => Path.Combine(_folder, "meters.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static StemMeter Stem(params string[] present) =>
        new(Song, "k|Vocal", present.ToHashSet(), new HashSet<string>());

    [Fact]
    public void What_was_applied_survives_a_restart()
    {
        Assert.True(new MeterStore(FilePath).Settle(Song, [Stem("6/8", "Odd bars")]));

        var record = new MeterStore(FilePath).Of("artist - title");

        Assert.Equal(["6/8", "Odd bars"], record?.Applied);
    }

    [Fact]
    public void Settling_the_same_stems_again_changes_nothing()
    {
        var store = new MeterStore(FilePath);

        store.Settle(Song, [Stem("4/4")]);

        Assert.False(store.Settle(Song, [Stem("4/4")]));
    }

    [Fact]
    public void A_song_whose_stems_carry_no_meter_gets_no_record()
    {
        var store = new MeterStore(FilePath);

        Assert.False(store.Settle(Song, []));
        Assert.Empty(store.Read());
    }

    [Fact]
    public void The_keywords_wanted_are_none_while_the_stems_contradict()
    {
        var store = new MeterStore(FilePath);

        store.Settle(Song, [Stem("3/4", "5/4")]);

        Assert.Empty(store.Wanted(Song));
        Assert.True(store.Of(Song)?.Contradicts);
    }

    [Fact]
    public void A_hand_mark_stands_until_the_song_has_a_meter() =>
        Assert.Equal(
            [TrackMark.OddBars],
            new MeterStore(FilePath).MarksFor(Song, [TrackMark.OddBars])
        );

    [Fact]
    public void Plain_four_four_takes_the_hand_mark_off_and_leaves_other_marks()
    {
        var store = new MeterStore(FilePath);

        store.Settle(Song, [Stem("4/4")]);

        Assert.Equal(
            [TrackMark.ChangesKey],
            store.MarksFor(Song, [TrackMark.OddBars, TrackMark.ChangesKey])
        );
    }

    [Theory]
    [InlineData("3/4")]
    [InlineData("Mixed")]
    public void Any_other_meter_puts_odd_bars_on_whatever_the_hand_says(string meter)
    {
        var store = new MeterStore(FilePath);

        store.Settle(Song, [Stem(meter)]);

        Assert.Equal([TrackMark.OddBars], store.MarksFor(Song, []));
    }

    [Fact]
    public void A_contradiction_shows_odd_bars()
    {
        var store = new MeterStore(FilePath);

        store.Settle(Song, [Stem("3/4", "5/4")]);

        Assert.Equal([TrackMark.OddBars], store.MarksFor(Song, []));
    }
}
