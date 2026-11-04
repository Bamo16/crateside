using Crateside.Core.Plans;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.WorkLists;

/// <summary>An added source renamed and moved to the library is still recognized as fetched.</summary>
public sealed class AddedSourceRenameRealLibraryTests
{
    [Fact]
    public void An_added_source_moved_to_the_library_is_still_held()
    {
        var library = RealPaths.Library();
        Assert.SkipUnless(TestLedger.HasReal, "No ledger on this machine.");

        var ledger = TestLedger.FromReal();

        var processed = ProcessedTracks.Scan(ledger, [library]);
        var inventories = WorkListService.InventoriesBySong(processed, ledger, PresetCatalog.Empty);

        var held = inventories
            .Values.Distinct()
            .SelectMany(inventory => inventory.Files)
            .Where(file => file.Kind is StemKind.Source)
            .Select(file => file.File.SourceTrackKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.SkipWhen(held.Count is 0, "No kept source has reached the library.");

        foreach (var upload in held)
        {
            var inventory = inventories[upload];
            var added = new AddedSource { TrackKey = upload, Label = "whatever the user typed" };

            Assert.True(inventory.Has(added), $"{upload} is on disk but reads as not fetched.");
        }
    }
}
