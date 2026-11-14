using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class WarpSettingsStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("warp-settings").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Nothing_chosen_reads_as_nothing_so_appsettings_stands() =>
        Assert.Null(new WarpSettingsStore(Path.Combine(_root, "none.json")).Read());

    [Theory]
    [InlineData(StarterWarps.Off)]
    [InlineData(StarterWarps.OnTheGrid)]
    public void A_choice_outlives_the_store_that_made_it(StarterWarps chosen)
    {
        var path = Path.Combine(_root, "choice.json");

        new WarpSettingsStore(path).Remember(chosen);

        Assert.Equal(chosen, new WarpSettingsStore(path).Read());
    }

    [Fact]
    public void A_mode_since_removed_reads_as_nothing_chosen()
    {
        var path = Path.Combine(_root, "old.json");
        File.WriteAllText(path, """{ "starterWarps": "TempoOnly" }""");

        Assert.Null(new WarpSettingsStore(path).Read());
    }

    [Fact]
    public void An_unreadable_file_reads_as_nothing_chosen()
    {
        var path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "{ not json");

        Assert.Null(new WarpSettingsStore(path).Read());
    }

    [Fact]
    public void The_clip_flag_is_unchosen_until_it_is_switched()
    {
        var path = Path.Combine(_root, "flag.json");

        Assert.Null(new WarpSettingsStore(path).ReadFlagClipOffBarLine());

        new WarpSettingsStore(path).RememberFlagClipOffBarLine(false);

        Assert.False(new WarpSettingsStore(path).ReadFlagClipOffBarLine());
    }

    [Fact]
    public void Choosing_one_setting_keeps_the_other()
    {
        var path = Path.Combine(_root, "both.json");
        var store = new WarpSettingsStore(path);

        store.Remember(StarterWarps.Off);
        store.RememberFlagClipOffBarLine(false);
        store.Remember(StarterWarps.OnTheGrid);

        Assert.Equal(StarterWarps.OnTheGrid, store.Read());
        Assert.False(store.ReadFlagClipOffBarLine());
    }
}
