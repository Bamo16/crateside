using System.Diagnostics;
using Crateside.Core.Ableton;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Ableton;

internal static class TestCollections
{
    public static CollectionSettingsStore Store(string folder, CollectionSettings? settings = null)
    {
        Directory.CreateDirectory(folder);

        CollectionSettingsStore store = new(
            Path.Combine(folder, "collection-settings.json"),
            TestLedger.Store()
        );
        store.Save(settings ?? CollectionSettings.Maintainers);

        return store;
    }

    public static int Number(BrowserCollection collection) =>
        CollectionSettings.Maintainers.NumberOf(collection) ?? throw new UnreachableException();
}
