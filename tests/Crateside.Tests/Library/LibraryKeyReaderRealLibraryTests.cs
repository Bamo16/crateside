using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Samples;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Library;

/// <summary>The key distribution over the real library.</summary>
public sealed class LibraryKeyReaderRealLibraryTests
{
    private static readonly CratesidePaths Paths = RealPaths.Create();

    [Fact]
    public void Every_track_is_counted_at_most_once()
    {
        SkipUnlessPresent();

        var distribution = Read();

        Assert.True(distribution.KeyedTrackCount <= distribution.TrackCount);
        Assert.True(distribution.TrackCount < distribution.FileCount);
    }

    [Fact]
    public void All_twenty_four_keys_are_offered_whether_or_not_they_are_used()
    {
        SkipUnlessPresent();

        Assert.Equal(24, Read().Counts.Count);
    }

    [Fact]
    public void The_index_and_the_ledger_never_disagree_about_a_key()
    {
        SkipUnlessPresent();

        var ledger = TestLedger.FromReal();
        var tags = new FileIndexReader(Paths.AbletonIndex).Read(Paths.LibraryFolder);

        var conflicts = Directory
            .EnumerateFiles(Paths.LibraryFolder, "*", SearchOption.AllDirectories)
            .Where(SampleFiles.IsAudio)
            .Select(file =>
                (
                    File: Path.GetFileName(file),
                    Applied: Applied(tags, file),
                    Verdict: Verdict(ledger, file)
                )
            )
            .Where(e => e.Applied is not null && e.Verdict is not null)
            .Where(e => !e.Applied!.Equals(e.Verdict, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            conflicts.Length is 0,
            $"{conflicts.Length} file(s) carry a key that is not the verdict: "
                + string.Join(
                    ", ",
                    conflicts
                        .Take(5)
                        .Select(e => $"{e.File} tagged {e.Applied}, judged {e.Verdict}")
                )
        );
    }

    [Fact]
    public void A_song_the_user_renamed_is_named_as_the_Tracks_page_names_it()
    {
        SkipUnlessPresent();
        Assert.SkipUnless(TestLedger.HasReal, "No ledger on this machine.");

        using var store = TestLedger.StoreReal();
        var ledger = store.Read();
        var paths = Options.Create(Paths);
        var songs = new LibraryKeyReader(
            store,
            new TrackInstructionStore(store),
            paths,
            new FileIndexReader(paths.Value.AbletonIndex),
            new FolderInfoReader(),
            new WarpTempoReader()
        )
            .Read()
            .Songs.ToDictionary(song => song.Song, StringComparer.OrdinalIgnoreCase);

        var renamed = new TrackInstructionStore(store)
            .Read()
            .Where(instruction => instruction.Name is { Length: > 0 })
            .Select(instruction =>
                (
                    instruction.Name,
                    Song: ledger.SongFor(instruction.TrackKey) ?? instruction.TrackKey
                )
            )
            .Where(pair => songs.ContainsKey(pair.Song))
            .ToArray();

        Assert.All(renamed, pair => Assert.Equal(pair.Name, songs[pair.Song].Name));
    }

    private static string? Applied(FolderInfo tags, string file) =>
        tags.TryGet(file, out var applied) && applied.IsKeyTagged ? applied.Key : null;

    private static string? Verdict(KeyLedger ledger, string file) =>
        StemTagReader.Read(file)?.Provenance is { SourceTrackKey: { } trackKey }
        && ledger.TryGetLatestByTrackKey(trackKey, out var run)
        && run.HasTaggableKey
        // A re-judged song's new verdict waits on the approval list (ADR 0052) before its files change.
        && ledger.Runs.Count(other => other.Song == run.Song && other.Restates is null) is 1
            ? run.Key
            : null;

    private static KeyDistribution Read()
    {
        var paths = Options.Create(Paths);
        var ledger = TestLedger.Store();

        return new LibraryKeyReader(
            ledger,
            new TrackInstructionStore(ledger),
            paths,
            new FileIndexReader(paths.Value.AbletonIndex),
            new FolderInfoReader(),
            new WarpTempoReader()
        ).Read();
    }

    private static void SkipUnlessPresent()
    {
        Assert.SkipUnless(
            Directory.Exists(Paths.LibraryFolder),
            $"{Paths.LibraryFolder} not present on this machine."
        );
        Assert.SkipUnless(
            Directory.Exists(Paths.AbletonIndex),
            "Ableton's index is not present on this machine."
        );
    }
}
