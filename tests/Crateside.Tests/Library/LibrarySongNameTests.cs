using System.Text.Json;
using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Library;

/// <summary>The name a library song is shown under follows a rename (#156).</summary>
public sealed class LibrarySongNameTests : IDisposable
{
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";
    private const string TrackKey = "abcdefghijk";
    private const string Judged = "GYARI (ココアシガレットP) - Seyana.";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public LibrarySongNameTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_song_is_named_as_it_was_judged_until_it_is_renamed()
    {
        Stem($"{Judged} (Vocal - Full).flac");

        Assert.Equal(Judged, Assert.Single(Read(Ledger()).Songs).Name);
    }

    [Fact]
    public void A_renamed_song_is_shown_under_its_new_name_and_credited_to_its_new_artist()
    {
        Stem("GYARI - Seyana (Vocal - Full).flac");
        var ledger = Ledger();
        new TrackInstructionStore(ledger).SetName(TrackKey, "GYARI - Seyana");

        var song = Assert.Single(Read(ledger).Songs);

        Assert.Equal("GYARI - Seyana", song.Name);
        Assert.Equal("GYARI", song.Artist);
    }

    [Fact]
    public void Reverting_the_rename_puts_the_judged_name_back()
    {
        Stem($"{Judged} (Vocal - Full).flac");
        var ledger = Ledger();
        var instructions = new TrackInstructionStore(ledger);
        instructions.SetName(TrackKey, "GYARI - Seyana");
        instructions.SetName(TrackKey, null);

        Assert.Equal(Judged, Assert.Single(Read(ledger).Songs).Name);
    }

    private KeyDistribution Read(LedgerStore ledger)
    {
        var paths = Options.Create(new CratesidePaths { LibraryFolder = _root });

        return new LibraryKeyReader(
            ledger,
            new TrackInstructionStore(ledger),
            paths,
            new FileIndexReader(paths.Value.AbletonIndex),
            new FolderInfoReader(),
            new WarpTempoReader()
        ).Read();
    }

    private void Stem(string name)
    {
        var path = Path.Combine(_root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = $"stemforge/0.4.0.0 | preset: Vocal - Full | source: {Upload}";
        file.Save();
    }

    private LedgerStore Ledger()
    {
        var jsonl = Path.Combine(_root, "ledger.jsonl");
        File.WriteAllText(
            jsonl,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-01T00:00:00+00:00",
                    song = Judged,
                    artist = "GYARI (ココアシガレットP)",
                    title = "Seyana.",
                    source_url = Upload,
                    verdict = new
                    {
                        key = "A minor",
                        confidence = "HIGH",
                        mode = "aeolian",
                    },
                }
            )
        );

        return TestLedger.Store(jsonl);
    }
}
