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
    [InlineData(StarterWarps.TempoOnly)]
    [InlineData(StarterWarps.OnTheGrid)]
    public void A_choice_outlives_the_store_that_made_it(StarterWarps chosen)
    {
        var path = Path.Combine(_root, "choice.json");

        new WarpSettingsStore(path).Remember(chosen);

        Assert.Equal(chosen, new WarpSettingsStore(path).Read());
    }

    [Fact]
    public void An_unreadable_file_reads_as_nothing_chosen()
    {
        var path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "{ not json");

        Assert.Null(new WarpSettingsStore(path).Read());
    }
}
