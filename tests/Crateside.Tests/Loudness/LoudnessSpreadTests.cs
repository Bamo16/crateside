using Crateside.Core;
using Crateside.Core.Loudness;

namespace Crateside.Tests.Loudness;

public sealed class LoudnessSpreadTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly LoudnessOptions _options = new();

    public LoudnessSpreadTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Gains_are_binned_by_the_decibel_with_0_dB_in_range()
    {
        var spread = await SpreadOf(
            ("A (Vocal - Full).flac", -19.5),
            ("B (Vocal - Full).flac", -19.5),
            ("C (Vocal - Full).flac", -10.5)
        );

        Assert.Equal(3, spread.Vocals.Count);
        Assert.Equal(
            [(-4, 1), (-3, 0), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 2)],
            [.. spread.Vocals.Bins.Select(bin => (bin.FromDb, bin.Count))]
        );
    }

    [Fact]
    public async Task Median_percentiles_and_the_beyond_counts_come_from_the_gains()
    {
        // gains +6, +4, 0, -2, -7 dB
        var spread = await SpreadOf(
            ("A (Instrumental - Full).flac", -20),
            ("B (Instrumental - Full).flac", -18),
            ("C (Instrumental - Full).flac", -14),
            ("D (Instrumental - Full).flac", -12),
            ("E (Instrumental - Full).flac", -7)
        );

        var group = spread.Instrumentals;

        Assert.Equal(0, group.Median!.Value, 6);
        Assert.Equal(-7 + (0.2 * 5), group.Low!.Value, 6);
        Assert.Equal(6 - (0.2 * 2), group.High!.Value, 6);
        Assert.Equal(3, group.Beyond(3));
        Assert.Equal(1, group.Beyond(6));
        Assert.Equal(["A", "B"], group.Boosts.Select(sample => sample.Name[..1]));
        Assert.Equal(["E", "D"], group.Cuts.Select(sample => sample.Name[..1]));
    }

    [Fact]
    public async Task A_sample_with_its_kinds_usual_preset_is_named_by_its_song_alone()
    {
        const string name = "Harry Nilsson - Without You (Instrumental - Full).flac";
        var reader = new LoudnessReader(new FakeEbur128(_ => -21));
        var path = Path.Combine(_root, name);

        File.Copy(Fixture, path);

        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = "Harry Nilsson - Without You";
            file.Tag.Performers = ["Harry Nilsson"];
            file.Tag.Comment =
                "stemforge/0.3.1.0 | preset: Instrumental - Full | source: https://www.youtube.com/watch?v=aaaaaaaaaaa";
            file.Save();
        }

        await reader.MeasureAsync(path, TestContext.Current.CancellationToken);

        var spread = LoudnessSpread.From(
            [path],
            reader,
            new ClipGains(reader, new CurrentOptions<LoudnessOptions>(() => _options))
        );

        Assert.Equal(
            "Harry Nilsson - Without You",
            Assert.Single(spread.Instrumentals.Boosts).Name
        );
    }

    [Fact]
    public async Task A_sample_below_the_floor_or_unread_is_counted_apart_from_the_histogram()
    {
        var path = Path.Combine(_root, "Quiet (Vocal - Full).flac");
        File.Copy(Fixture, path);
        var spread = await SpreadOf(
            ("Soft (Vocal - Full).flac", -30),
            ("Loud (Vocal - Full).flac", -14)
        );
        var unread = new LoudnessReader(new FakeEbur128(_ => -14));

        var notRead = LoudnessSpread.From(
            [path],
            unread,
            new ClipGains(unread, new CurrentOptions<LoudnessOptions>(() => _options))
        );

        Assert.Equal(1, spread.Vocals.BelowFloor);
        Assert.Equal(1, spread.Vocals.Count);
        Assert.Equal(1, notRead.Vocals.NotRead);
        Assert.Equal(0, notRead.Vocals.Count);
    }

    [Fact]
    public async Task Drums_are_left_out()
    {
        var spread = await SpreadOf(("Song (Drums).flac", -20));

        Assert.Equal(0, spread.Vocals.Count + spread.Instrumentals.Count);
        Assert.Equal(0, spread.Vocals.NotRead + spread.Instrumentals.NotRead);
    }

    private static readonly string Fixture = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "silence.flac"
    );

    private async Task<LoudnessSpread> SpreadOf(params (string Name, double Lufs)[] samples)
    {
        var lufs = samples.ToDictionary(sample => sample.Name, sample => sample.Lufs);
        var reader = new LoudnessReader(new FakeEbur128(path => lufs[Path.GetFileName(path)]));
        List<string> paths = [];

        foreach (var (name, _) in samples)
        {
            var path = Path.Combine(_root, name);
            File.Copy(Fixture, path);
            paths.Add(path);
            await reader.MeasureAsync(path, TestContext.Current.CancellationToken);
        }

        return LoudnessSpread.From(
            paths,
            reader,
            new ClipGains(reader, new CurrentOptions<LoudnessOptions>(() => _options))
        );
    }
}
