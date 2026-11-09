using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Genres;

namespace Crateside.Tests.Ableton;

public sealed class FileTagsReadingTests
{
    private static readonly CratesidePaths Paths = RealPaths.Create();

    [Fact]
    public void Recombines_the_two_key_keywords_into_the_ledger_spelling()
    {
        var tags = TestTags.Of("a.flac", ["Key|G♯", "Key|Major", "Type|Instrumental"]);

        Assert.Equal("G# major", tags.Key); // Unicode ♯ normalized, mode lowercased
        Assert.Equal("Instrumental", tags.Type);
        Assert.True(tags.IsKeyTagged);
        Assert.False(tags.HasPartialKey);
    }

    [Fact]
    public void A_tonic_with_no_mode_is_not_a_key()
    {
        // Real data: two Fedde Le Grand stems carry Key|D and no Major/Minor keyword.
        var tags = TestTags.Of("a.flac", ["Key|D", "Type|Instrumental"]);

        Assert.Null(tags.Key);
        Assert.False(tags.IsKeyTagged);
        Assert.True(tags.HasPartialKey);
    }

    [Fact]
    public void An_entry_with_keywords_but_no_key_is_not_key_tagged()
    {
        var tags = TestTags.Of("Loop.flac", ["Type|Loop"]);

        Assert.Null(tags.Key);
        Assert.False(tags.HasPartialKey);
        Assert.Equal("Loop", tags.Type);
    }

    [Fact]
    public void Reads_the_genres_of_the_entry()
    {
        var tags = TestTags.Of(
            "a.flac",
            ["Key|A", "Key|Minor", "Genres|Hip Hop", "Genres|Pop", "Genre|Rock"]
        );

        Assert.Equal(["Hip Hop", "Pop"], tags.Genres);
    }

    [Theory]
    [InlineData("Mood|Dark")]
    [InlineData("Key|C#")]
    [InlineData("Type|Drums")]
    [InlineData("Genre|Rock")]
    [InlineData("Genres|")]
    public void A_keyword_outside_cratesides_vocabulary_is_not_known(string keyword) =>
        Assert.False(CratesideKeywords.IsKnown(keyword));

    [Fact]
    public void Every_keyword_already_in_the_library_is_one_crateside_would_write()
    {
        // The vocabulary is a closed set derived from this library, so it must accept all of it.
        var reader = new FolderInfoReader();

        var keywords = new[] { Paths.StagingFolder, Paths.LibraryFolder }
            .Where(Directory.Exists)
            .SelectMany(root => reader.Read(root).Entries)
            .SelectMany(entry => entry.Keywords)
            // The retired group is taken off by the next backing pass, not accepted again.
            .Where(keyword => !GenreVocabulary.IsRetiredKeyword(keyword))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.SkipWhen(keywords.Count is 0, "no tagged stems on this machine");

        var refused = keywords.Where(k => !CratesideKeywords.IsKnown(k)).ToList();

        Assert.True(refused.Count is 0, $"crateside would refuse: {string.Join(", ", refused)}");
    }

    [Fact]
    public void Keys_in_the_library_recombine_into_one_of_the_24_ledger_spellings()
    {
        Assert.SkipUnless(
            Directory.Exists(Paths.LibraryFolder),
            $"{Paths.LibraryFolder} not present."
        );

        string[] tonics = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        var valid = tonics
            .SelectMany(t => new[] { $"{t} major", $"{t} minor" })
            .ToHashSet(StringComparer.Ordinal);

        var keys = new FolderInfoReader()
            .Read(Paths.LibraryFolder)
            .Entries.Where(entry => entry.IsKeyTagged)
            .Select(entry => entry.Key!)
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.Contains(key, valid));
    }

    [Fact]
    public void Half_tagged_files_in_the_library_are_partial_not_keyed()
    {
        Assert.SkipUnless(
            Directory.Exists(Paths.LibraryFolder),
            $"{Paths.LibraryFolder} not present."
        );

        // Asserts the shape, not the count: the user may fix them at any time.
        var partial = new FolderInfoReader()
            .Read(Paths.LibraryFolder)
            .Entries.Where(entry => entry.HasPartialKey)
            .ToArray();

        Assert.All(partial, entry => Assert.Null(entry.Key));
        Assert.All(partial, entry => Assert.False(entry.IsKeyTagged));
    }

    /// <summary>The index spells tonics with U+266F, which must not reach the ledger's comparison.</summary>
    [Fact]
    public void Keys_from_lives_index_arrive_in_the_ledgers_ascii_spelling()
    {
        Assert.SkipUnless(RealLibrary.HasStems(Paths.LibraryFolder), "no stems on this machine.");
        Assert.SkipUnless(Directory.Exists(Paths.AbletonIndex), "Live's index is not present.");

        var keyed = new FileIndexReader(Paths.AbletonIndex)
            .Read(Paths.LibraryFolder)
            .Entries.Where(entry => entry.IsKeyTagged)
            .ToArray();

        Assert.NotEmpty(keyed);
        Assert.All(keyed, entry => Assert.DoesNotContain('♯', entry.Key!));
        Assert.All(keyed, entry => Assert.DoesNotContain('♭', entry.Key!));
    }
}
