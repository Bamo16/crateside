using System.Text.Json;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Ableton;

public sealed class CollectionSettingsStoreTests : IDisposable
{
    private readonly string _root = Directory
        .CreateTempSubdirectory("collection-settings")
        .FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_fresh_install_has_every_meaning_off_and_stays_that_way_once_it_has_history()
    {
        var path = Path.Combine(_root, "fresh.json");
        var ledger = TestLedger.Store();

        Assert.Equal(new CollectionSettings(), new CollectionSettingsStore(path, ledger).Read());

        var later = OneRun();

        Assert.Equal(new CollectionSettings(), new CollectionSettingsStore(path, later).Read());
    }

    [Fact]
    public void An_install_with_ledger_history_keeps_the_numbers_it_always_used()
    {
        var path = Path.Combine(_root, "existing.json");
        var ledger = OneRun();

        var settings = new CollectionSettingsStore(path, ledger).Read();

        Assert.Equal(CollectionSettings.Maintainers, settings);
        Assert.Equal(
            [2, 4, 5, 6, 7],
            Enum.GetValues<BrowserCollection>().Select(meaning => settings.NumberOf(meaning)!.Value)
        );
    }

    [Fact]
    public void A_choice_outlives_the_store_that_made_it_including_none()
    {
        var path = Path.Combine(_root, "choice.json");
        var chosen = new CollectionSettings { Warped = 3, OddBars = null };

        new CollectionSettingsStore(path, OneRun()).Save(chosen);

        Assert.Equal(chosen, new CollectionSettingsStore(path, OneRun()).Read());
    }

    [Fact]
    public void An_unreadable_file_reads_as_every_meaning_off_and_is_not_overwritten()
    {
        var path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "{ not json");

        var settings = new CollectionSettingsStore(path, OneRun()).Read();

        Assert.Equal(new CollectionSettings(), settings);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    private LedgerStore OneRun()
    {
        var jsonl = Path.Combine(_root, "ledger.jsonl");
        File.WriteAllText(
            jsonl,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-01T00:00:00+00:00",
                    song = "A - T",
                    artist = "A",
                    title = "T",
                    source_url = "https://www.youtube.com/watch?v=abcdefghijk",
                    verdict = new { key = "A minor", confidence = "HIGH" },
                }
            )
        );

        return TestLedger.Store(jsonl);
    }
}
