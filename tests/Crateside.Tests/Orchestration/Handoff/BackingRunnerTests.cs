using System.Text.Json;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Tests.Ableton;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>What a backing pass writes over an entry that already exists.</summary>
public sealed class BackingRunnerTests : IDisposable
{
    private const string File1 = "Lil Wayne - A Milli (A Cappella).flac";
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";

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
    public void An_entry_that_already_has_a_key_does_not_need_one()
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
        Assert.False(Needs(Tags("Type|Vocal", "Mood|Dark")));
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
        var runner = Runner();

        Assert.Empty(runner.Unbacked(Path.Combine(_root, "gone"), TestLedger.From("no-such-file")));
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
        var merged = Merge(Tags("Key|G♯", "Key|Minor", "Type|Vocal"), ["Genres|Hip Hop"]);

        Assert.Equal(["Key|G♯", "Key|Minor", "Type|Vocal", "Genres|Hip Hop"], merged);
    }

    [Fact]
    public void A_genre_crateside_wrote_and_no_longer_wants_is_taken_off()
    {
        var merged = Merge(
            Tags("Key|G♯", "Key|Minor", "Genres|Pop"),
            ["Genres|Hip Hop"],
            retired: ["Genres|Pop"]
        );

        Assert.Equal(["Key|G♯", "Key|Minor", "Genres|Hip Hop"], merged);
    }

    [Fact]
    public void A_genres_keyword_crateside_did_not_write_is_left_alone()
    {
        var merged = Merge(
            Tags("Key|G♯", "Key|Minor", "Genres|Funk"),
            ["Genres|Hip Hop"],
            retired: ["Genres|Pop"]
        );

        Assert.Equal(["Key|G♯", "Key|Minor", "Genres|Funk", "Genres|Hip Hop"], merged);
    }

    [Fact]
    public void A_genre_already_on_the_entry_is_not_written_twice()
    {
        var merged = Merge(Tags("Key|G♯", "Key|Minor", "Genres|Pop"), ["Genres|Pop"]);

        Assert.Equal(["Key|G♯", "Key|Minor", "Genres|Pop"], merged);
    }

    [Fact]
    public void The_retired_genre_group_is_taken_off_and_its_genre_written_in_the_new_one()
    {
        var merged = Merge(
            Tags("Key|G♯", "Key|Minor", "Genre|Hip Hop", "Genres|Funk"),
            ["Genres|Hip Hop"]
        );

        Assert.Equal(["Key|G♯", "Key|Minor", "Genres|Funk", "Genres|Hip Hop"], merged);
    }

    [Fact]
    public void A_genre_the_user_took_off_is_not_put_back()
    {
        var merged = Merge(
            TestTags.Of(File1, ["Key|G♯", "Key|Minor"], ["Genres|Rock"]),
            ["Genres|Rock"]
        );

        Assert.DoesNotContain("Genres|Rock", merged);
    }

    [Fact]
    public void Only_a_keyed_entry_takes_a_genre()
    {
        Assert.True(BackingRunner.CanTakeOwnedTags(Tags("Key|G♯", "Key|Minor")));
        Assert.False(BackingRunner.CanTakeOwnedTags(Tags("Type|Vocal")));
        Assert.False(BackingRunner.CanTakeOwnedTags(null));
        Assert.False(BackingRunner.CanTakeOwnedTags(Tags("Key|G♯", "Key|Minor", "Mood|Dark")));
        Assert.True(BackingRunner.CanTakeOwnedTags(Tags("Key|G♯", "Key|Minor", "Genre|Rap")));
    }

    [Fact]
    public void Genre_and_familiarity_each_replace_only_their_own_group()
    {
        var merged = Merge(
            Tags("Key|G♯", "Key|Minor", "Genres|Pop", "Familiarity|Obscure"),
            ["Familiarity|Mainstream"]
        );

        Assert.Equal(["Key|G♯", "Key|Minor", "Genres|Pop", "Familiarity|Mainstream"], merged);
    }

    [Fact]
    public void A_familiarity_the_user_took_off_is_not_put_back()
    {
        var merged = Merge(
            TestTags.Of(File1, ["Key|G♯", "Key|Minor"], ["Familiarity|Mainstream"]),
            ["Genres|Rock", "Familiarity|Mainstream"]
        );

        Assert.Equal(["Key|G♯", "Key|Minor", "Genres|Rock"], merged);
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

    [Fact]
    public void A_correction_replaces_the_whole_key_and_keeps_everything_else()
    {
        var merged = BackingRunner.Merged(
            Tags("Key|F♯", "Key|Major", "Type|Vocal", "Genres|Pop"),
            ["Key|D♯", "Key|Minor", "Type|Instrumental"],
            correcting: true
        );

        Assert.Equal(["Type|Vocal", "Genres|Pop", "Key|D♯", "Key|Minor"], merged);
    }

    [Fact]
    public void A_correction_writes_the_whole_key_even_where_part_of_it_was_once_taken_off()
    {
        var merged = BackingRunner.Merged(
            TestTags.Of(File1, ["Key|F♯", "Key|Major"], ["Key|Minor"]),
            ["Key|D♯", "Key|Minor"],
            correcting: true
        );

        Assert.Equal(["Key|D♯", "Key|Minor"], merged);
    }

    [Fact]
    public void A_key_that_differs_from_the_verdict_is_rewritten_and_logged()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|C♯", "Key|Major", "Type|Vocal"],
                },
            ]
        );
        var log = new ListLogger();

        var outcome = new BackingRunner(new FolderInfoReader(), new FolderInfoWriter(), log).Run(
            _root,
            Ledger("HIGH"),
            backKeys: false
        );

        Assert.IsType<TagWriteOutcome.Written>(outcome);
        Assert.True(new FolderInfoReader().Read(_root).TryGet(stem, out var tags));
        Assert.Equal("A minor", tags.Key);
        Assert.Contains("Type|Vocal", tags.Keywords);
        Assert.Equal([$"retagged {Path.GetFileName(stem)}: C# major → A minor"], log.Messages);
    }

    [Fact]
    public void A_sample_with_the_retired_genre_ends_with_the_new_one_and_a_genres_tag_of_lives_is_left_alone()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|A", "Key|Minor", "Genre|Hip Hop", "Genres|Funk"],
                },
            ]
        );

        var outcome = Runner()
            .Run(
                _root,
                Ledger("HIGH"),
                ownedOf: _ => ["Genres|Hip Hop"],
                backKeys: false,
                retiredOf: _ => ["Genres|Pop"]
            );

        Assert.IsType<TagWriteOutcome.Written>(outcome);
        Assert.True(new FolderInfoReader().Read(_root).TryGet(stem, out var tags));
        Assert.Contains("Genres|Hip Hop", tags.Keywords);
        Assert.Contains("Genres|Funk", tags.Keywords);
        Assert.DoesNotContain(tags.Keywords, keyword => keyword.StartsWith("Genre|"));
    }

    [Fact]
    public void A_genre_crateside_wrote_and_has_since_retired_is_taken_off_the_file()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|A", "Key|Minor", "Genres|Pop", "Genres|Funk"],
                },
            ]
        );

        Runner()
            .Run(
                _root,
                Ledger("HIGH"),
                ownedOf: _ => ["Genres|Hip Hop"],
                backKeys: false,
                retiredOf: _ => ["Genres|Pop"]
            );

        Assert.True(new FolderInfoReader().Read(_root).TryGet(stem, out var tags));
        Assert.DoesNotContain("Genres|Pop", tags.Keywords);
        Assert.Contains("Genres|Funk", tags.Keywords);
        Assert.Contains("Genres|Hip Hop", tags.Keywords);
    }

    [Fact]
    public void A_key_that_matches_the_verdict_is_left_alone()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [new() { RelativePath = Path.GetFileName(stem), Keywords = ["Key|A", "Key|Minor"] }]
        );

        Assert.Empty(Runner().Unbacked(_root, Ledger("HIGH")));
    }

    [Fact]
    public void A_key_is_left_as_found_where_the_verdict_is_withheld()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [new() { RelativePath = Path.GetFileName(stem), Keywords = ["Key|C♯", "Key|Major"] }]
        );

        Assert.Empty(Runner().Unbacked(_root, Ledger("LOW")));
    }

    [Fact]
    public void A_key_is_left_as_found_when_corrections_are_off()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [new() { RelativePath = Path.GetFileName(stem), Keywords = ["Key|C♯", "Key|Major"] }]
        );

        Assert.Empty(Runner().Unbacked(_root, Ledger("HIGH"), correctKeys: false));
    }

    [Fact]
    public void An_imported_file_keeps_the_users_Collections_and_loses_its_warp_state()
    {
        var stem = LibraryStem();
        var index = new FolderInfo(
            _root,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|A", "Key|Minor"],
                    Collections = [1, (int)BrowserCollection.Warped],
                },
            ]
        );

        var assignment = Assert.Single(Runner().Unbacked(_root, Ledger("HIGH"), index: index));

        Assert.Equal([1], assignment.Collections);
        Assert.Equal(["Key|A", "Key|Minor", "Type|Vocal"], assignment.Keywords);
    }

    [Fact]
    public void A_backed_entry_takes_its_tracks_key_caveat_and_keeps_its_keywords()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [new() { RelativePath = Path.GetFileName(stem), Keywords = ["Key|A", "Key|Minor"] }]
        );

        var assignment = Assert.Single(
            Runner().Unbacked(_root, Ledger("LOW"), index: FolderInfo.Empty(_root))
        );

        Assert.Equal([(int)BrowserCollection.KeyCaveat], assignment.Collections);
        Assert.Null(assignment.Keywords);
    }

    [Fact]
    public void A_pass_for_one_song_leaves_the_files_of_another_alone()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [new() { RelativePath = Path.GetFileName(stem), Keywords = ["Key|A", "Key|Minor"] }]
        );
        var runner = Runner();
        var ledger = Ledger("LOW");

        Assert.Empty(runner.Unbacked(_root, ledger, index: FolderInfo.Empty(_root), song: "B - U"));
        Assert.Single(
            runner.Unbacked(_root, ledger, index: FolderInfo.Empty(_root), song: "a - t")
        );
    }

    [Fact]
    public void Without_the_index_Collections_are_left_alone()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|A", "Key|Minor"],
                    Collections = [(int)BrowserCollection.CheckBeat],
                },
            ]
        );

        Assert.Empty(Runner().Unbacked(_root, Ledger("LOW")));
    }

    [Fact]
    public void A_modal_verdict_gets_its_mode_beside_the_collapse()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [new() { RelativePath = Path.GetFileName(stem), Keywords = ["Key|A", "Key|Minor"] }]
        );

        var assignment = Assert.Single(
            Runner().Unbacked(_root, Ledger("HIGH", "dorian"), ownedOf: _ => [])
        );

        Assert.Equal(["Key|A", "Key|Minor", "Key|Dorian"], assignment.Keywords);
    }

    [Fact]
    public void A_plain_verdict_takes_an_old_mode_tag_off()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|A", "Key|Minor", "Key|Dorian"],
                },
            ]
        );

        var assignment = Assert.Single(Runner().Unbacked(_root, Ledger("HIGH"), ownedOf: _ => []));

        Assert.Equal(["Key|A", "Key|Minor"], assignment.Keywords);
    }

    [Fact]
    public void A_changed_mode_replaces_the_old_mode_tag()
    {
        var stem = LibraryStem();
        new FolderInfoWriter().Apply(
            _root,
            [
                new()
                {
                    RelativePath = Path.GetFileName(stem),
                    Keywords = ["Key|A", "Key|Minor", "Key|Dorian"],
                },
            ]
        );

        var assignment = Assert.Single(
            Runner().Unbacked(_root, Ledger("HIGH", "phrygian"), ownedOf: _ => [])
        );

        Assert.Equal(["Key|A", "Key|Minor", "Key|Phrygian"], assignment.Keywords);
    }

    private static BackingRunner Runner() =>
        new(new FolderInfoReader(), new FolderInfoWriter(), NullLogger<BackingRunner>.Instance);

    private sealed class ListLogger : ILogger<BackingRunner>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }

    private string LibraryStem()
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, "Song (Vocal - Full).flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = $"stemforge/0.4.0.0 | preset: Vocal - Full | source: {Upload}";
        file.Save();

        return path;
    }

    private KeyLedger Ledger(string confidence, string mode = "aeolian")
    {
        var jsonl = Path.Combine(_root, "ledger.jsonl");
        File.WriteAllText(
            jsonl,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-01T00:00:00+00:00",
                    song = "A - T",
                    artist = "A",
                    title = "T",
                    source_url = Upload,
                    verdict = new
                    {
                        key = "A minor",
                        confidence,
                        mode,
                    },
                }
            )
        );

        return TestLedger.From(jsonl);
    }

    private static FileTags Tags(params string[] keywords) => TestTags.Of(File1, keywords);

    private static bool Needs(FileTags existing) => BackingRunner.NeedsAKey(existing);

    private static IReadOnlyList<string> Merge(
        FileTags? existing,
        IReadOnlyList<string> keywords,
        IReadOnlyList<string>? retired = null
    ) => BackingRunner.Merged(existing, keywords, retired: retired);
}
