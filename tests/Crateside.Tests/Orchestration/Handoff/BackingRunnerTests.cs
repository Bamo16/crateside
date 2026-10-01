using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Tests.Ableton;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>What a backing pass writes over an entry that already exists.</summary>
public sealed class BackingRunnerTests : IDisposable
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
        Assert.False(Needs(Tags("Key|G♯")));
        Assert.False(Needs(Tags("Key|Minor")));
    }

    [Fact]
    public void An_entry_carrying_a_keyword_the_writer_cannot_write_is_left_alone()
    {
        Assert.False(Needs(Tags("Type|Vocal", "Genre|Rap")));
    }

    [Fact]
    public void The_key_is_added_and_everything_already_there_survives()
    {
        var merged = Merge(Tags("Type|Vocal"), ["Key|G♯", "Key|Minor", "Type|Instrumental"]);

        Assert.Contains("Key|G♯", merged);
        Assert.Contains("Key|Minor", merged);

        // The hand-applied type wins over the one crateside derives.
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
            keyword => Assert.True(CratesideKeywords.IsKnown(keyword))
        );
    }

    [Fact]
    public void A_root_that_is_not_there_yields_nothing()
    {
        var runner = new BackingRunner(new FolderInfoReader(), new FolderInfoWriter());

        Assert.Empty(runner.Unbacked(Path.Combine(_root, "gone"), KeyLedger.Read("no-such-file")));
    }

    [Fact]
    public void A_keyword_the_user_took_off_is_not_put_back()
    {
        // Shaped after Porter Robinson - Language, where Key|Major was taken off and Key|Minor set.
        var merged = Merge(
            TestTags.Of(File1, ["Type|Vocal"], ["Key|Major"]),
            ["Key|F♯", "Key|Major", "Type|Instrumental"]
        );

        Assert.Contains("Key|F♯", merged);
        Assert.DoesNotContain("Key|Major", merged);
    }

    [Fact]
    public void A_type_the_user_took_off_is_not_put_back_either()
    {
        var merged = Merge(
            TestTags.Of(File1, [], ["Type|Instrumental"]),
            ["Key|G♯", "Key|Minor", "Type|Instrumental"]
        );

        Assert.Contains("Key|G♯", merged);
        Assert.DoesNotContain("Type|Instrumental", merged);
    }

    [Fact]
    public void A_settled_genre_is_added_to_a_keyed_entry_and_everything_else_survives()
    {
        var merged = Merge(Tags("Key|G♯", "Key|Minor", "Type|Vocal"), ["Genre|Hip Hop"]);

        Assert.Equal(["Key|G♯", "Key|Minor", "Type|Vocal", "Genre|Hip Hop"], merged);
    }

    [Fact]
    public void A_changed_genre_replaces_the_one_crateside_wrote_before()
    {
        var merged = Merge(Tags("Key|G♯", "Key|Minor", "Genre|Pop"), ["Genre|K-Pop"]);

        Assert.Contains("Genre|K-Pop", merged);
        Assert.DoesNotContain("Genre|Pop", merged);
    }

    [Fact]
    public void A_genre_the_user_took_off_is_not_put_back()
    {
        var merged = Merge(
            TestTags.Of(File1, ["Key|G♯", "Key|Minor"], ["Genre|Rock"]),
            ["Genre|Rock"]
        );

        Assert.DoesNotContain("Genre|Rock", merged);
    }

    [Fact]
    public void Only_a_keyed_entry_takes_a_genre()
    {
        Assert.True(BackingRunner.CanTakeOwnedTags(Tags("Key|G♯", "Key|Minor")));
        Assert.False(BackingRunner.CanTakeOwnedTags(Tags("Type|Vocal")));
        Assert.False(BackingRunner.CanTakeOwnedTags(null));
        Assert.False(BackingRunner.CanTakeOwnedTags(Tags("Key|G♯", "Key|Minor", "Genre|Rap")));
    }

    [Fact]
    public void Genre_and_familiarity_each_replace_only_their_own_group()
    {
        var merged = Merge(
            Tags("Key|G♯", "Key|Minor", "Genre|Pop", "Familiarity|Obscure"),
            ["Familiarity|Mainstream"]
        );

        Assert.Equal(["Key|G♯", "Key|Minor", "Genre|Pop", "Familiarity|Mainstream"], merged);
    }

    [Fact]
    public void A_familiarity_the_user_took_off_is_not_put_back()
    {
        var merged = Merge(
            TestTags.Of(File1, ["Key|G♯", "Key|Minor"], ["Familiarity|Mainstream"]),
            ["Genre|Rock", "Familiarity|Mainstream"]
        );

        Assert.Equal(["Key|G♯", "Key|Minor", "Genre|Rock"], merged);
    }

    [Fact]
    public void Both_familiarities_are_keywords_the_writer_accepts() =>
        Assert.All(
            Crateside.Core.Familiarity.FamiliarityVocabulary.All,
            value =>
                Assert.True(
                    CratesideKeywords.IsKnown(
                        Crateside.Core.Familiarity.FamiliarityVocabulary.Keyword(value)
                    )
                )
        );

    [Fact]
    public void Every_genre_on_the_list_is_a_keyword_the_writer_accepts()
    {
        Assert.All(
            Crateside.Core.Genres.GenreVocabulary.All,
            genre =>
                Assert.True(
                    CratesideKeywords.IsKnown(Crateside.Core.Genres.GenreVocabulary.Keyword(genre))
                )
        );
    }

    private static FileTags Tags(params string[] keywords) => TestTags.Of(File1, keywords);

    private static bool Needs(FileTags existing) => BackingRunner.NeedsAKey(existing);

    private static IReadOnlyList<string> Merge(
        FileTags? existing,
        IReadOnlyList<string> keywords
    ) => BackingRunner.Merged(existing, keywords);
}
