using Crateside.Core.Orchestration.Handoff;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Handoff;

public sealed class TagStoreBackupsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Each_write_keeps_a_copy_of_the_store_and_only_the_newest_are_kept()
    {
        var library = Path.Combine(_root, "Library");
        var store = Path.Combine(library, "Ableton Folder Info");
        Directory.CreateDirectory(store);
        File.WriteAllText(Path.Combine(store, "store.xmp"), "first");

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var backups = new TagStoreBackups(Path.Combine(_root, "backups"), time, keep: 2);

        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(store, "store.xmp"), $"write {i}");
            backups.Save(library);
            time.Advance(TimeSpan.FromMinutes(1));
        }

        var kept = Directory.GetDirectories(Path.Combine(_root, "backups")).Order().ToArray();

        Assert.Equal(2, kept.Length);
        Assert.Equal("write 2", File.ReadAllText(Path.Combine(kept[^1], "store.xmp")));
    }
}
