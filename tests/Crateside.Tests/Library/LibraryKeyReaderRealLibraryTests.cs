using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Library;

/// <summary>
/// The key distribution over the real `Mashup Samples`. Machine-bound by design; skips where
/// the library or Ableton's index is absent.
/// </summary>
public class LibraryKeyReaderRealLibraryTests
{
    private static readonly CratesidePaths Paths = new();

    [Fact]
    public void Every_file_is_counted_exactly_once()
    {
        SkipUnlessPresent();

        var distribution = Read();

        Assert.Equal(
            distribution.FileCount,
            distribution.Counts.Sum(c => c.Files) + distribution.Unkeyed.Count
        );
    }

    [Fact]
    public void All_twenty_four_keys_are_offered_whether_or_not_they_are_used()
    {
        SkipUnlessPresent();

        Assert.Equal(24, Read().Counts.Count);
    }

    /// <summary>
    /// The measurement the panel's single source rests on
    /// ([ADR 0013](../../../docs/adr/0013-the-index-is-the-tag-state-and-the-xmp-may-never-catch-up.md)).
    /// The original spec for this panel required a union of the ledger and the tag store; it was
    /// dropped because the index covers the ledger entirely. If this ever fails, the panel is
    /// under-counting and the union has to come back.
    /// </summary>
    [Fact]
    public void The_index_keys_every_file_the_ledger_can_key()
    {
        SkipUnlessPresent();

        var ledger = KeyLedger.Read(Paths.Ledger);
        var tags = new AbletonIndexReader(Options.Create(Paths)).Read(Paths.MashupSamples);

        var missed = Directory
            .EnumerateFiles(Paths.MashupSamples, "*", SearchOption.AllDirectories)
            .Where(StagingBoard.IsStemFile)
            .Where(file => LedgerKeys(ledger, file) && !IndexKeys(tags, file))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(
            missed.Length is 0,
            $"The ledger keys {missed.Length} file(s) the index does not, so counting from the "
                + $"index alone under-reports: {string.Join(", ", missed.Take(5))}"
        );
    }

    private static bool LedgerKeys(KeyLedger ledger, string file) =>
        StemTagReader.Read(file)?.Provenance is { SourceVideoId: { } videoId }
        && ledger.TryGetLatestByVideoId(videoId, out var run)
        && run.HasTaggableKey;

    private static bool IndexKeys(AbletonFolderInfo tags, string file) =>
        tags.TryGet(file, out var applied) && applied.IsKeyTagged;

    private static KeyDistribution Read()
    {
        var paths = Options.Create(Paths);

        return new LibraryKeyReader(paths, new AbletonIndexReader(paths)).Read();
    }

    private static void SkipUnlessPresent()
    {
        Assert.SkipUnless(
            Directory.Exists(Paths.MashupSamples),
            $"{Paths.MashupSamples} not present on this machine."
        );
        Assert.SkipUnless(
            Directory.Exists(Paths.AbletonIndex),
            "Ableton's index is not present on this machine."
        );
    }
}
