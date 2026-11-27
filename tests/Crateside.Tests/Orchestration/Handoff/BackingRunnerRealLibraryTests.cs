using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Samples;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

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

    private static IReadOnlyList<TagAssignment> Unbacked(
        string library,
        bool correctKeys = false,
        WarpTempoReader? tempos = null
    ) =>
        new BackingRunner(
            new FolderInfoReader(),
            new FolderInfoWriter(),
            new CollectionSettingsStore(
                Path.Combine(Path.GetTempPath(), "crateside-tests-absent", "collections.json"),
                TestLedger.Store()
            ),
            NullLogger<BackingRunner>.Instance,
            tempos
        ).Unbacked(library, TestLedger.FromReal(), correctKeys: correctKeys);

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
    public void After_the_pass_every_keyed_file_has_one_camelot_number_and_a_letter_unless_modal()
    {
        var library = Library();
        var backed = new FolderInfoReader().Read(library);
        var planned = Unbacked(library, correctKeys: true)
            .ToDictionary(a => a.RelativePath, StringComparer.OrdinalIgnoreCase);
        var keyed = 0;

        foreach (var path in Directory.EnumerateFiles(library, "*", SearchOption.AllDirectories))
        {
            if (
                !backed.TryGet(path, out var tags)
                || !tags.IsKeyTagged
                || !SampleFiles.IsAudio(path)
            )
                continue;

            keyed++;
            var after =
                planned.TryGetValue(Path.GetRelativePath(library, path), out var assignment)
                && assignment.Keywords is { } keywords
                    ? keywords
                    : tags.Keywords;
            var camelot = after.Where(CamelotTags.IsKeyword).ToArray();
            var modal = after.Any(ModeTags.IsKeyword);

            Assert.True(
                camelot.Count(k => int.TryParse(k["Camelot|".Length..], out _)) is 1,
                $"{path}: {string.Join(", ", camelot)}"
            );
            Assert.Equal(!modal ? 1 : 0, camelot.Count(k => k is "Camelot|A" or "Camelot|B"));
        }

        Assert.True(keyed > 0);
    }

    [Fact]
    public void After_the_pass_every_warped_keyed_file_has_exactly_two_tempo_tags_and_no_other_has_any()
    {
        var library = Library();
        var backed = new FolderInfoReader().Read(library);
        var tempos = new WarpTempoReader();
        var planned = Unbacked(library, correctKeys: true, tempos)
            .ToDictionary(a => a.RelativePath, StringComparer.OrdinalIgnoreCase);
        var perBand = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int warped = 0,
            unwarped = 0;

        foreach (var path in Directory.EnumerateFiles(library, "*", SearchOption.AllDirectories))
        {
            if (
                !backed.TryGet(path, out var tags)
                || !tags.IsKeyTagged
                || !SampleFiles.IsAudio(path)
            )
                continue;

            var after =
                planned.TryGetValue(Path.GetRelativePath(library, path), out var assignment)
                && assignment.Keywords is { } keywords
                    ? keywords
                    : tags.Keywords;
            var tempo = after.Where(TempoTags.IsKeyword).ToArray();

            if (tempos.Read(path) is { } warp)
            {
                warped++;
                Assert.True(tempo.Length is 2, $"{path}: {string.Join(", ", tempo)}");
                Assert.Equal(TempoTags.For(warp.Bpm).Order(), tempo.Order());

                foreach (var tag in tempo)
                    perBand[tag] = perBand.GetValueOrDefault(tag) + 1;
            }
            else
            {
                unwarped++;
                Assert.Empty(tempo);
            }
        }

        Assert.True(warped > 0);
        TestContext.Current.SendDiagnosticMessage(
            $"warped {warped}, no warp {unwarped}; files per tag: "
                + string.Join("; ", perBand.Select(pair => $"{pair.Key} = {pair.Value}"))
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
