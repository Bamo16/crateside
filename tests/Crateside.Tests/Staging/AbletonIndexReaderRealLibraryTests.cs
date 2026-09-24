using Crateside.Core;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Staging;

/// <summary>Ableton's own index, asserted against the real database on this machine.</summary>
public class AbletonIndexReaderRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string MashupRoot = @"P:\RYAN\Ableton\Mashup Samples";

    private static readonly CratesidePaths Paths = new();

    public static TheoryData<string> Roots => [StagingRoot, MashupRoot];

    [Theory]
    [MemberData(nameof(Roots))]
    public void Reads_a_real_root_while_Live_is_running(string root)
    {
        SkipUnlessPresent(root);

        var info = Read(root);

        Assert.NotEmpty(info.Entries);
        Assert.All(info.Entries, e => Assert.NotEmpty(e.RelativePath));
        Assert.All(info.Entries, e => Assert.NotEmpty(e.Keywords));
    }

    /// <summary>The index spells tonics with U+266F, which must not reach a caller.</summary>
    [Theory]
    [MemberData(nameof(Roots))]
    public void Keys_arrive_in_the_ledgers_ascii_spelling(string root)
    {
        SkipUnlessPresent(root);

        var keyed = Read(root).Entries.Where(e => e.IsKeyTagged).ToArray();

        Assert.NotEmpty(keyed);
        Assert.All(keyed, e => Assert.DoesNotContain('♯', e.Key!));
        Assert.All(keyed, e => Assert.DoesNotContain('♭', e.Key!));
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public void Finds_an_entry_by_absolute_path_as_well_as_relative(string root)
    {
        SkipUnlessPresent(root);

        var entry = Read(root).Entries.First();

        Assert.True(Read(root).TryGet(entry.RelativePath, out var byRelative));
        Assert.True(Read(root).TryGet(Path.Combine(root, entry.RelativePath), out var byAbsolute));
        Assert.Equal(byRelative.RelativePath, byAbsolute.RelativePath);
    }

    [Fact]
    public void A_folder_Ableton_has_never_been_shown_reads_empty()
    {
        Assert.SkipUnless(
            Directory.Exists(Paths.AbletonIndex),
            "Ableton's index is not present on this machine."
        );

        Assert.Empty(Read(Path.GetTempPath()).Entries);
    }

    [Fact]
    public void An_absent_index_reads_empty_rather_than_throwing()
    {
        var reader = new AbletonIndexReader(
            Options.Create(
                new CratesidePaths
                {
                    AbletonIndex = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
                }
            )
        );

        Assert.Empty(reader.Read(MashupRoot).Entries);
    }

    private static AbletonFolderInfo Read(string root) =>
        new AbletonIndexReader(Options.Create(Paths)).Read(root);

    private static void SkipUnlessPresent(string root)
    {
        Assert.SkipUnless(RealLibrary.HasStems(root), $"{root} holds no stems on this machine.");
        Assert.SkipUnless(
            Directory.Exists(Paths.AbletonIndex),
            "Ableton's index is not present on this machine."
        );
    }
}
