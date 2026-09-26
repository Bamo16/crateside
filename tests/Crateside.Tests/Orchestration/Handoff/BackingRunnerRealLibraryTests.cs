using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Handoff;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>What a backing pass would do to the user's actual library, without writing to it.</summary>
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

        // Having a key is the test, not having an entry.
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

                Assert.All(was.Keywords, keyword => Assert.Contains(keyword, assignment.Keywords));
            }
        );
    }

    [Fact]
    public void Every_entry_it_would_write_is_a_keyword_ableton_already_knows()
    {
        Assert.SkipUnless(Available, "The library and the ledger must be present.");

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
