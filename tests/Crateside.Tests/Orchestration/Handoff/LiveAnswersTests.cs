using System.Text.Json;
using System.Xml.Linq;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Vocals;
using Crateside.Tests.Ableton;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>A tag added or removed in Live is the person's answer (ADR 0087).</summary>
public sealed class LiveAnswersTests : IDisposable
{
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";
    private const string Song = "A - T";
    private const string Vocal = "Song (Vocal - Full).flac";
    private const string Instrumental = "Song (Instrumental - Full).flac";

    private static readonly DateTimeOffset At = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly LedgerStore _store = new(LedgerStore.InMemory, TimeProvider.System);
    private readonly GenreStore _genres;
    private readonly FamiliarityStore _familiarity;
    private readonly VocalAnswerStore _vocals;
    private readonly LiveAnswers _answers;
    private readonly BackingRunner _runner;

    public LiveAnswersTests()
    {
        _genres = new(_store);
        _familiarity = new(_store);
        _vocals = new(_store);
        _answers = new(_genres, _familiarity, _vocals);
        Directory.CreateDirectory(_root);
        _runner = new(
            new FolderInfoReader(),
            new FolderInfoWriter(),
            TestCollections.Store(_root),
            NullLogger<BackingRunner>.Instance
        );
        Stem(Vocal, "Vocal - Full");
        Stem(Instrumental, "Instrumental - Full");
    }

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_genre_added_on_one_sample_becomes_the_songs_and_is_written_onto_the_other()
    {
        Store(Vocal, "Key|A", "Key|Minor", "Type|Vocal", "Genres|Meme");
        Store(Instrumental, "Key|A", "Key|Minor", "Type|Instrumental");

        Assert.Equal(1, Record());

        Assert.Equal(["Meme"], _genres.Read()[Song].Mine);

        Back();

        Assert.Contains("Genres|Meme", Keywords(Instrumental));
    }

    [Fact]
    public void A_genre_in_Lives_index_alone_is_found_before_the_store_catches_up()
    {
        Store(Vocal, "Key|A", "Key|Minor");
        var index = new FolderInfo(
            _root,
            [new() { RelativePath = Vocal, Keywords = ["Key|A", "Key|Minor", "Genres|Jazz"] }]
        );

        Record(index);

        Assert.Equal(["Jazz"], _genres.Read()[Song].Genres);
    }

    [Fact]
    public void A_genre_crateside_wrote_is_not_taken_for_an_answer()
    {
        _genres.Hear(Song, new GenreHearing(["pop"], null), At);
        _genres.MarkTagged([(Song, ["Pop"])]);
        Store(Vocal, "Key|A", "Key|Minor", "Genres|Pop");

        Assert.Equal(0, Record());
        Assert.False(_genres.Read()[Song].IsTheUsersAnswer);
    }

    [Fact]
    public void A_familiarity_added_beside_the_one_crateside_wrote_is_the_persons_answer()
    {
        Measure(listeners: 5000, pageviews: 30000);
        Store(Vocal, "Key|A", "Key|Minor", "Familiarity|Mainstream", "Familiarity|Niche");

        Record();

        Assert.Equal("Niche", _familiarity.Read()[Song].Settled);
        Assert.Equal("Niche", _familiarity.Read()[Song].Written);
    }

    [Fact]
    public void A_familiarity_on_a_song_crateside_has_written_none_for_is_left_alone()
    {
        Store(Vocal, "Key|A", "Key|Minor", "Familiarity|Niche");

        Assert.Equal(0, Record());
        Assert.Empty(_familiarity.Read());
    }

    [Fact]
    public void A_familiarity_crateside_wrote_and_the_person_removed_is_never_written_back()
    {
        Measure(listeners: 5000, pageviews: 30000);
        Store(Vocal, "Key|A", "Key|Minor", "Type|Vocal");
        Store(Instrumental, "Key|A", "Key|Minor", "Familiarity|Mainstream");
        Hide(Vocal, "Familiarity|Mainstream");

        Record();
        Back();

        Assert.Null(_familiarity.Read()[Song].Written);
        Assert.DoesNotContain("Familiarity|Mainstream", Keywords(Vocal));
        Assert.DoesNotContain("Familiarity|Mainstream", Keywords(Instrumental));
    }

    [Fact]
    public void A_genre_removed_in_Live_is_not_assigned_to_the_song_again()
    {
        _genres.Hear(Song, new GenreHearing(["pop"], null), At);
        _genres.MarkTagged([(Song, ["Pop"])]);
        Store(Vocal, "Key|A", "Key|Minor");
        Store(Instrumental, "Key|A", "Key|Minor", "Genres|Pop");
        Hide(Vocal, "Genres|Pop");

        Record();
        Back();

        Assert.Empty(_genres.Read()[Song].Genres);
        Assert.DoesNotContain("Genres|Pop", Keywords(Instrumental));
    }

    [Fact]
    public void A_choice_made_in_crateside_after_a_removal_stands()
    {
        _genres.Hear(Song, new GenreHearing(["pop"], null), At);
        _genres.MarkTagged([(Song, ["Pop"])]);
        Store(Vocal, "Key|A", "Key|Minor");
        Hide(Vocal, "Genres|Pop");
        Record();

        _genres.Choose(Song, "Pop");

        Assert.Equal(0, Record());
        Assert.Equal(["Pop"], _genres.Read()[Song].Genres);
    }

    [Fact]
    public void A_later_answer_in_crateside_replaces_an_earlier_one_from_Live()
    {
        Store(Vocal, "Key|A", "Key|Minor", "Genres|Jazz");
        Record();
        _genres.MarkTagged([(Song, ["Jazz"])]);

        _genres.Choose(Song, "Rock");

        Assert.Equal(["Rock"], _genres.Read()[Song].Genres);
        Assert.Equal(["Jazz"], _genres.Read()[Song].Retired);
    }

    [Fact]
    public void A_later_answer_from_Live_replaces_an_earlier_one_in_crateside()
    {
        _genres.Choose(Song, "Rock");
        _genres.MarkTagged([(Song, ["Rock"])]);
        Store(Vocal, "Key|A", "Key|Minor", "Genres|Rock", "Genres|Jazz");

        Record();

        Assert.Equal(["Jazz"], _genres.Read()[Song].Genres);
        Assert.Equal(["Rock"], _genres.Read()[Song].Retired);
    }

    [Fact]
    public void A_vocal_stem_tagged_Lead_in_Live_keeps_it_for_that_stem_alone()
    {
        Store(Vocal, "Key|A", "Key|Minor", "Type|Vocal", "Type|Vocal|Lead");
        Store(Instrumental, "Key|A", "Key|Minor", "Type|Instrumental");

        Record();

        var answer = Assert.Single(_vocals.Read());
        Assert.Equal("abcdefghijk|Vocal - Full", answer.Key);
        Assert.Equal("Lead", answer.Value);
    }

    [Fact]
    public void A_vocal_tag_taken_off_in_Live_takes_the_answer_back()
    {
        _vocals.Settle("abcdefghijk|Vocal - Full", "Sparse");
        Store(Vocal, "Key|A", "Key|Minor", "Type|Vocal");
        Hide(Vocal, "Type|Vocal|Sparse");

        Record();

        Assert.Empty(_vocals.Read());
    }

    [Fact]
    public void A_stem_tagged_Lead_still_takes_crateside_s_own_tags()
    {
        Store(Vocal, "Key|A", "Key|Minor", "Type|Vocal", "Type|Vocal|Lead");

        _runner.Run(
            _root,
            Ledger(),
            ownedOf: _ => ["Genres|Pop"],
            backKeys: false,
            correctKeys: false
        );

        Assert.Equal(
            ["Key|A", "Key|Minor", "Type|Vocal", "Type|Vocal|Lead", "Genres|Pop"],
            Keywords(Vocal)
        );
    }

    [Fact]
    public void The_type_is_the_plain_one_whatever_order_the_subtag_is_in()
    {
        var tags = new FileTags
        {
            RelativePath = Vocal,
            Keywords = ["Type|Vocal|Lead", "Type|Vocal"],
        };

        Assert.Equal("Vocal", tags.Type);
    }

    [Fact]
    public void The_two_stems_tagged_by_hand_on_2026_11_09_read_as_the_persons_answers()
    {
        var library = RealPaths.Library();
        Assert.SkipUnless(TestLedger.HasReal, "No ledger on this machine.");

        var edits = _runner.LiveEdits(
            library,
            TestLedger.FromReal(),
            null,
            _ => new HashSet<string>()
        );

        Assert.Contains(edits, edit => edit is { Keyword: "Type|Vocal|Lead", Removed: false });
        Assert.Contains(edits, edit => edit is { Keyword: "Type|Vocal|Sparse", Removed: false });
    }

    private int Record(FolderInfo? index = null) =>
        _answers.Record(_runner.LiveEdits(_root, Ledger(), index, _answers.WrittenFor));

    private void Back()
    {
        var genres = _genres.Read();
        var reach = _familiarity.Read();

        _genres.MarkTagged(genres.Values.Select(record => (record.Song, record.Genres)));
        _familiarity.MarkTagged(
            reach
                .Values.Where(record => record.Written is not null)
                .Select(record => (record.Song, record.Written!))
        );
        genres = _genres.Read();
        reach = _familiarity.Read();

        _runner.Run(
            _root,
            Ledger(),
            ownedOf: _ =>
                [
                    .. genres.GetValueOrDefault(Song)?.Genres.Select(GenreVocabulary.Keyword) ?? [],
                    .. reach.GetValueOrDefault(Song)?.Written is { } known
                        ? [FamiliarityVocabulary.Keyword(known)]
                        : Array.Empty<string>(),
                ],
            backKeys: false,
            correctKeys: false,
            retiredOf: _ =>
                [
                    .. genres.GetValueOrDefault(Song)?.Retired.Select(GenreVocabulary.Keyword)
                        ?? [],
                    .. reach.GetValueOrDefault(Song)?.Retired.Select(FamiliarityVocabulary.Keyword)
                        ?? [],
                ]
        );
    }

    private void Measure(int listeners, int pageviews)
    {
        _familiarity.Measure(Song, new FamiliarityReading(listeners, pageviews, null), At, 1);
        _familiarity.MarkTagged([(Song, _familiarity.Read()[Song].Written!)]);
    }

    private void Stem(string name, string preset)
    {
        var path = Path.Combine(_root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = $"stemforge/0.4.0.0 | preset: {preset} | source: {Upload}";
        file.Save();
    }

    private void Store(string name, params string[] keywords)
    {
        var existing = new FolderInfoReader().Read(_root);
        var others = existing
            .Entries.Where(entry => entry.RelativePath != name)
            .Select(entry => new TagAssignment
            {
                RelativePath = entry.RelativePath,
                Keywords = entry.Keywords,
            });

        new FolderInfoWriter().Apply(
            _root,
            [.. others, new TagAssignment { RelativePath = name, Keywords = keywords }]
        );
    }

    private void Hide(string name, string keyword)
    {
        var xmp = Directory.GetFiles(Path.Combine(_root, "Ableton Folder Info"), "*.xmp").Single();
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        XNamespace abl = "https://ns.ableton.com/xmp/fs-resources/1.0/";
        var document = XDocument.Load(xmp);
        var item = document
            .Descendants(abl + "filePath")
            .Single(path => path.Value == name)
            .Parent!;

        item.Add(
            new XElement(
                abl + "hideKeywords",
                new XElement(rdf + "Bag", new XElement(rdf + "li", keyword))
            )
        );
        document.Save(xmp);
    }

    private IReadOnlyList<string> Keywords(string name) =>
        new FolderInfoReader().Read(_root).TryGet(name, out var tags) ? tags.Keywords : [];

    private KeyLedger Ledger()
    {
        var jsonl = Path.Combine(_root, "ledger.jsonl");
        File.WriteAllText(
            jsonl,
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-01T00:00:00+00:00",
                    song = Song,
                    artist = "A",
                    title = "T",
                    source_url = Upload,
                    verdict = new
                    {
                        key = "A minor",
                        confidence = "HIGH",
                        mode = "aeolian",
                    },
                }
            )
        );

        return TestLedger.From(jsonl);
    }
}
