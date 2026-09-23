using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Staging;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// What a backing pass would do to the user's actual library, asserted without writing to it.
/// Real data because the question is whether the gap is real and closable here, on this machine
/// (ADR 0023).
/// </summary>
public class BackingRunnerRealLibraryTests
{
    private const string LibraryRoot = @"P:\RYAN\Ableton\Mashup Samples";
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    private static bool Available => Directory.Exists(LibraryRoot) && File.Exists(LedgerPath);

    private static IReadOnlyList<TagAssignment> Unbacked() =>
        new BackingRunner(new AbletonFolderInfoReader(), new AbletonFolderInfoWriter()).Unbacked(
            LibraryRoot,
            KeyLedger.Read(LedgerPath)
        );

    [Fact]
    public void No_key_the_store_already_holds_is_offered_again()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

        var backed = new AbletonFolderInfoReader().Read(LibraryRoot);

        // Having an entry is not the test — an entry with a Type and no key is one this pass
        // completes. Having a key is.
        Assert.DoesNotContain(
            Unbacked(),
            a =>
                backed.TryGet(Path.Combine(LibraryRoot, a.RelativePath), out var tags)
                && tags.IsKeyTagged
        );
    }

    [Fact]
    public void An_entry_it_completes_keeps_every_keyword_that_was_there()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

        var backed = new AbletonFolderInfoReader().Read(LibraryRoot);

        Assert.All(
            Unbacked(),
            assignment =>
            {
                if (!backed.TryGet(Path.Combine(LibraryRoot, assignment.RelativePath), out var was))
                    return;

                // The writer replaces the whole bag, so a keyword dropped here is destroyed.
                Assert.All(was.Keywords, keyword => Assert.Contains(keyword, assignment.Keywords));
            }
        );
    }

    [Fact]
    public void Every_entry_it_would_write_is_a_keyword_ableton_already_knows()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

        // A keyword Ableton does not have is not rejected -- it silently becomes a second,
        // permanent user tag -- so the writer refuses the whole pass over one bad word.
        Assert.All(
            Unbacked().SelectMany(a => a.Keywords),
            keyword =>
                Assert.True(
                    AbletonFolderInfoWriter.IsValidKeyword(keyword),
                    $"'{keyword}' would be refused, taking the whole pass with it."
                )
        );
    }

    [Fact]
    public void Every_entry_it_would_write_carries_a_whole_key()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

        // A tonic without a mode is the partial key the audit reports, and writing one would
        // create the finding rather than clear it.
        Assert.All(
            Unbacked(),
            assignment =>
                Assert.Equal(
                    2,
                    assignment.Keywords.Count(k => k.StartsWith("Key|", StringComparison.Ordinal))
                )
        );
    }

    [Fact]
    public void The_pass_names_each_file_once()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

        var assignments = Unbacked();

        Assert.Equal(
            assignments.Count,
            assignments
                .Select(a => a.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        );
    }
}
