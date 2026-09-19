using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>
/// Whether an added source the user has renamed and moved to the library is still recognised as
/// fetched. Only real data answers this: the rename is something a person does in Ableton, and
/// the fetch decision is made from what the scan can still see afterwards.
/// </summary>
public class AddedSourceRenameRealLibraryTests
{
    private const string LibraryRoot = @"P:\RYAN\Ableton\Mashup Samples";
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    private static bool Available => Directory.Exists(LibraryRoot) && File.Exists(LedgerPath);

    [Fact]
    public void An_added_source_moved_to_the_library_is_still_held()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

        var ledger = KeyLedger.Read(LedgerPath);

        // The library alone, which is the state the moment the user moves the file out of staging
        // and before anything is fetched again.
        var processed = ProcessedTracks.Scan(ledger, [LibraryRoot]);
        var inventories = QueueService.InventoriesBySong(processed, ledger, PresetCatalog.Empty);

        var held = inventories
            .Values.Distinct()
            .SelectMany(inventory => inventory.Files)
            .Where(file => file.Kind is StemKind.Source)
            .Select(file => file.File.SourceVideoId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.SkipWhen(held.Count is 0, "No kept source has reached the library.");

        // Every kept source in the library must answer Has() for its own upload, whatever the
        // user renamed the file to. The label is only consulted when provenance is absent.
        foreach (var upload in held)
        {
            var inventory = inventories[upload];
            var added = new AddedSource { VideoId = upload, Label = "whatever the user typed" };

            Assert.True(inventory.Has(added), $"{upload} is on disk but reads as not fetched.");
        }
    }
}
