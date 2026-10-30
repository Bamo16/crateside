using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>What a backing pass would do to the user's actual library, without writing to it.</summary>
public sealed class BackingRunnerRealLibraryTests
{
    private static string Library()
    {
        var library = RealPaths.Library();
        Assert.SkipUnless(TestLedger.HasReal, "No ledger on this machine.");

        return library;
    }

    private static IReadOnlyList<TagAssignment> Unbacked(string library) =>
        new BackingRunner(new FolderInfoReader(), new FolderInfoWriter()).Unbacked(
            library,
            TestLedger.FromReal()
        );

    [Fact]
    public void No_key_the_store_already_holds_is_offered_again()
    {
        var library = Library();
        var backed = new FolderInfoReader().Read(library);

        // Having a key is the test, not having an entry.
        Assert.DoesNotContain(
            Unbacked(library),
            a =>
                backed.TryGet(Path.Combine(library, a.RelativePath), out var tags)
                && tags.IsKeyTagged
        );
    }

    [Fact]
    public void An_entry_it_completes_keeps_every_keyword_that_was_there()
    {
        var library = Library();
        var backed = new FolderInfoReader().Read(library);

        Assert.All(
            Unbacked(library),
            assignment =>
            {
                if (!backed.TryGet(Path.Combine(library, assignment.RelativePath), out var was))
                    return;

                Assert.All(
                    was.Keywords,
                    keyword => Assert.Contains(keyword, assignment.Keywords ?? [])
                );
            }
        );
    }

    [Fact]
    public void Every_entry_it_would_write_is_a_keyword_ableton_already_knows()
    {
        var library = Library();

        Assert.All(
            Unbacked(library).SelectMany(a => a.Keywords ?? []),
            keyword =>
                Assert.True(
                    CratesideKeywords.IsKnown(keyword),
                    $"'{keyword}' would be refused, taking the whole pass with it."
                )
        );
    }

    [Fact]
    public void Every_entry_it_would_write_carries_a_whole_key()
    {
        var library = Library();

        Assert.All(
            Unbacked(library),
            assignment =>
                Assert.Equal(
                    2,
                    (assignment.Keywords ?? []).Count(k =>
                        k.StartsWith("Key|", StringComparison.Ordinal)
                    )
                )
        );
    }

    [Fact]
    public void The_pass_names_each_file_once()
    {
        var library = Library();

        var assignments = Unbacked(library);

        Assert.Equal(
            assignments.Count,
            assignments
                .Select(a => a.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        );
    }
}
