using Crateside.Core.Plans;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.WorkLists;

/// <summary>The inventory lookup run against the user's actual library and ledger.</summary>
public sealed class InventoryRealLibraryTests
{
    private static string[] Roots()
    {
        string[] roots = [RealPaths.Staging(), RealPaths.Library()];
        Assert.SkipUnless(TestLedger.HasReal, "No ledger on this machine.");

        return roots;
    }

    [Fact]
    public void Every_upload_of_a_cut_song_reaches_that_songs_stems()
    {
        var roots = Roots();
        var ledger = TestLedger.FromReal();
        var processed = ProcessedTracks.Scan(ledger, roots);
        var inventories = WorkListService.InventoriesBySong(processed, ledger, PresetCatalog.Empty);

        string Song(string videoId) => ledger.SongFor(videoId) ?? videoId;

        var cut = processed.StemsByVideoId.Keys.Select(Song).ToHashSet(StringComparer.Ordinal);

        // An upload the ledger files under a song that has stems.
        var stranded = ledger
            .Runs.Select(run => run.SourceVideoId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(videoId => cut.Contains(Song(videoId)))
            .Where(videoId => inventories.GetValueOrDefault(videoId) is null or { Files.Count: 0 })
            .ToArray();

        Assert.Empty(stranded);
    }

    [Fact]
    public void An_upload_with_no_file_of_its_own_still_sees_the_songs_presets()
    {
        var roots = Roots();
        var ledger = TestLedger.FromReal();
        var processed = ProcessedTracks.Scan(ledger, roots);
        var inventories = WorkListService.InventoriesBySong(processed, ledger, PresetCatalog.Empty);

        var withoutFilesOfTheirOwn = inventories
            .Keys.Where(videoId => !processed.StemsByVideoId.ContainsKey(videoId))
            .ToArray();

        Assert.SkipWhen(
            withoutFilesOfTheirOwn.Length is 0,
            "No song in this library is judged under more than one upload."
        );

        Assert.All(withoutFilesOfTheirOwn, videoId => Assert.NotEmpty(inventories[videoId].Files));
    }
}
