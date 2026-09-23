using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Staging;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// What a backing pass writes over an entry that already exists, which is the whole risk in it:
/// the writer replaces an entry's keyword bag, so anything not carried across is destroyed.
/// </summary>
public class BackingRunnerTests : IDisposable
{
    private const string File1 = "Lil Wayne - A Milli (A Cappella).flac";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_key_the_store_already_holds_is_never_rewritten()
    {
        Assert.False(Needs(Tags("Key|G♯", "Key|Minor", "Type|Vocal")));
    }

    [Fact]
    public void An_entry_with_a_type_and_no_key_is_given_one()
    {
        Assert.True(Needs(Tags("Type|Vocal")));
    }

    [Fact]
    public void Half_a_key_is_left_for_a_human()
    {
        // The audit reports it. Choosing the missing half is a guess about which half is right.
        Assert.False(Needs(Tags("Key|G♯")));
        Assert.False(Needs(Tags("Key|Minor")));
    }

    [Fact]
    public void An_entry_carrying_a_keyword_the_writer_cannot_write_is_left_alone()
    {
        // Carrying it across would have the writer refuse the whole pass; dropping it would lose
        // what the user put there. Skipping the file costs nothing either way.
        Assert.False(Needs(Tags("Type|Vocal", "Genre|Rap")));
    }

    [Fact]
    public void The_key_is_added_and_everything_already_there_survives()
    {
        var merged = Merge(Tags("Type|Vocal"), ["Key|G♯", "Key|Minor", "Type|Instrumental"]);

        Assert.Contains("Key|G♯", merged);
        Assert.Contains("Key|Minor", merged);

        // The hand-applied type wins: an added source has no preset, so the one crateside derives
        // is "Instrumental" for everything, which is what put the wrong tag here to begin with.
        Assert.Contains("Type|Vocal", merged);
        Assert.DoesNotContain("Type|Instrumental", merged);
    }

    [Fact]
    public void A_file_with_no_entry_is_written_exactly_as_computed()
    {
        string[] computed = ["Key|G♯", "Key|Minor", "Type|Instrumental"];

        Assert.Equal(computed, Merge(existing: null, computed));
    }

    [Fact]
    public void Everything_it_would_write_is_a_keyword_ableton_already_knows()
    {
        Assert.All(
            Merge(Tags("Type|Vocal"), ["Key|G♯", "Key|Minor"]),
            keyword => Assert.True(AbletonFolderInfoWriter.IsValidKeyword(keyword))
        );
    }

    [Fact]
    public void A_root_that_is_not_there_yields_nothing()
    {
        var runner = new BackingRunner(
            new AbletonFolderInfoReader(),
            new AbletonFolderInfoWriter()
        );

        Assert.Empty(runner.Unbacked(Path.Combine(_root, "gone"), KeyLedger.Read("no-such-file")));
    }

    private static AbletonFileTags Tags(params string[] keywords) =>
        AbletonFileTags.FromKeywords(File1, keywords);

    private static bool Needs(AbletonFileTags existing) => BackingRunner.NeedsAKey(existing);

    private static IReadOnlyList<string> Merge(
        AbletonFileTags? existing,
        IReadOnlyList<string> keywords
    ) => BackingRunner.Merged(existing, keywords);
}
