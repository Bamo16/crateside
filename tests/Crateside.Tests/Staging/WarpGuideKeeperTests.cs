using Crateside.Core;
using Crateside.Core.Staging;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Staging;

public sealed class WarpGuideKeeperTests : IDisposable
{
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly WarpGuideKeeper _keeper;

    public WarpGuideKeeperTests()
    {
        _paths = new CratesidePaths
        {
            SampleStaging = Path.Combine(_root, "Sample Staging"),
            MashupSamples = Path.Combine(_root, "Mashup Samples"),
            KeptWarpGuides = Path.Combine(_root, "Drums"),
        };
        Directory.CreateDirectory(_paths.SampleStaging);
        Directory.CreateDirectory(_paths.MashupSamples);

        _keeper = new WarpGuideKeeper(
            _paths,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero))
        );
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_filed_tracks_guide_moves_out_of_staging_with_its_sidecar()
    {
        Stem(_paths.MashupSamples, "Song (Instrumental - Full).flac", "Instrumental - Full");
        var guide = Stem(_paths.SampleStaging, "Song (Drums).flac", "Drums - htdemucs_ft");
        File.WriteAllBytes($"{guide}.asd", [7, 7, 7]);

        var kept = Assert.Single(_keeper.Keep());

        var to = Path.Combine(_paths.KeptWarpGuides, "Song (Drums).flac");
        Assert.Equal(new GuideKeeping.Kept(guide, to), kept);
        Assert.False(File.Exists(guide));
        Assert.False(File.Exists($"{guide}.asd"));
        Assert.True(File.Exists(to));
        Assert.Equal([7, 7, 7], File.ReadAllBytes($"{to}.asd"));
    }

    [Fact]
    public void A_guide_stays_while_any_of_its_tracks_samples_are_still_staged()
    {
        Stem(_paths.MashupSamples, "Song (Instrumental - Full).flac", "Instrumental - Full");
        Stem(_paths.SampleStaging, "Song (Vocal - Full).flac", "Vocal - Full");
        var guide = Stem(_paths.SampleStaging, "Song (Drums).flac", "Drums - htdemucs_ft");

        Assert.Empty(_keeper.Keep());
        Assert.True(File.Exists(guide));
    }

    [Fact]
    public void A_guide_whose_track_never_reached_Mashup_Samples_stays()
    {
        var guide = Stem(_paths.SampleStaging, "Song (Drums).flac", "Drums - htdemucs_ft");
        Stem(
            _paths.MashupSamples,
            "Other (Instrumental - Full).flac",
            "Instrumental - Full",
            "https://www.youtube.com/watch?v=zyxwvutsrqp"
        );

        Assert.Empty(_keeper.Keep());
        Assert.True(File.Exists(guide));
    }

    [Fact]
    public void A_guide_kept_before_is_never_overwritten()
    {
        Directory.CreateDirectory(_paths.KeptWarpGuides);
        var earlier = Path.Combine(_paths.KeptWarpGuides, "Song (Drums).flac");
        File.WriteAllBytes(earlier, [1]);
        Stem(_paths.MashupSamples, "Song (Instrumental - Full).flac", "Instrumental - Full");
        var guide = Stem(_paths.SampleStaging, "Song (Drums).flac", "Drums - htdemucs_ft");

        var kept = Assert.IsType<GuideKeeping.Kept>(Assert.Single(_keeper.Keep()));

        Assert.Equal(
            Path.Combine(_paths.KeptWarpGuides, "Song (Drums) (2026-10-02).flac"),
            kept.To
        );
        Assert.Equal([1], File.ReadAllBytes(earlier));
        Assert.False(File.Exists(guide));
    }

    private static string Stem(string root, string name, string preset, string source = Upload)
    {
        var path = Path.Combine(root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = $"stemforge/0.4.0.0 | preset: {preset} | source: {source}";
        file.Save();

        return path;
    }
}
