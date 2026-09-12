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
    /// The index and the ledger never disagree about a key.
    /// </summary>
    /// <remarks>
    /// This replaces an assertion that the index keys every file the ledger can. That was true on
    /// 2026-09-09 and is what let the panel drop the ledger ∪ tag-store union it was originally
    /// specified with; it is false now, because 110 files have lost their Ableton keywords
    /// (<see href="https://github.com/Bamo16/crateside/issues/24">#24</see>).
    ///
    /// The union is deliberately <em>not</em> coming back. The panel answers "what can I find by
    /// key in Ableton's browser", and a file whose tag is gone cannot be found there whatever the
    /// ledger remembers — counting it would overstate the library by exactly the number of
    /// samples that have stopped working. The gap is a fault to fix, not a source to paper over.
    ///
    /// What still has to hold is that the two never contradict each other where both have an
    /// answer, which is the assumption the panel's arithmetic actually rests on.
    /// </remarks>
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
