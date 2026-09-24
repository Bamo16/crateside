using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Library;

/// <summary>The key distribution over the real <c>Mashup Samples</c>.</summary>
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

    [Fact]
    public void The_index_and_the_ledger_never_disagree_about_a_key()
    {
        SkipUnlessPresent();

        var ledger = KeyLedger.Read(Paths.Ledger);
        var tags = new AbletonIndexReader(Options.Create(Paths)).Read(Paths.MashupSamples);

        var conflicts = Directory
            .EnumerateFiles(Paths.MashupSamples, "*", SearchOption.AllDirectories)
            .Where(StagingBoard.IsStemFile)
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

    private static string? Applied(AbletonFolderInfo tags, string file) =>
        tags.TryGet(file, out var applied) && applied.IsKeyTagged ? applied.Key : null;

    private static string? Verdict(KeyLedger ledger, string file) =>
        StemTagReader.Read(file)?.Provenance is { SourceVideoId: { } videoId }
        && ledger.TryGetLatestByVideoId(videoId, out var run)
        && run.HasTaggableKey
            ? run.Key
            : null;

    private static KeyDistribution Read()
    {
        var paths = Options.Create(Paths);

        return new LibraryKeyReader(
            paths,
            new AbletonIndexReader(paths),
            new AbletonFolderInfoReader()
        ).Read();
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
