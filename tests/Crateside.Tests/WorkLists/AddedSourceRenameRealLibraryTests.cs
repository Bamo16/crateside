using Crateside.Core.Ledger;
using Crateside.Core.Plans;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.WorkLists;

/// <summary>An added source renamed and moved to the library is still recognised as fetched.</summary>
public sealed class AddedSourceRenameRealLibraryTests
{
    private const string LibraryRoot = @"P:\RYAN\Ableton\Mashup Samples";

    private static bool Available => Directory.Exists(LibraryRoot) && TestLedger.HasReal;

    [Fact]
    public void An_added_source_moved_to_the_library_is_still_held()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

        var ledger = TestLedger.FromReal();

        var processed = ProcessedTracks.Scan(ledger, [LibraryRoot]);
        var inventories = WorkListService.InventoriesBySong(processed, ledger, PresetCatalog.Empty);

        var held = inventories
            .Values.Distinct()
            .SelectMany(inventory => inventory.Files)
            .Where(file => file.Kind is StemKind.Source)
            .Select(file => file.File.SourceVideoId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.SkipWhen(held.Count is 0, "No kept source has reached the library.");

        foreach (var upload in held)
        {
            var inventory = inventories[upload];
            var added = new AddedSource { VideoId = upload, Label = "whatever the user typed" };

            Assert.True(inventory.Has(added), $"{upload} is on disk but reads as not fetched.");
        }
    }
}
