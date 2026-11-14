using System.Net;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class PageChoicesTests : IDisposable
{
    private const string TrackKey = "IijgrkA_P50";
    private const string Tab = "https://www.hooktheory.com/theorytab/view/billy-joel/piano-man";
    private const string Wrong = "https://www.hooktheory.com/theorytab/view/someone/else";
    private const string Candidate =
        "https://www.hooktheory.com/theorytab/view/yoko-shimomura/forest-maze";

    // Two detectors on A minor, keydetect confident: with no page the rules rule MEDIUM, with a page HIGH.
    private const string Evidence = """
        {
          "algorithms": {
            "ffmpeg_keydetect": { "key": "A minor", "r": 0.9 },
            "madmom": { "key": "A minor" },
            "essentia_edma": { "key": "D major" }
          }
        }
        """;

    private static readonly WorkOrder Order = new()
    {
        TrackKey = TrackKey,
        Title = "Artist - Song",
        SourceUrl = $"https://music.youtube.com/watch?v={TrackKey}",
        Destination = StemDestination.UnresolvedKey,
    };

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(
        DateTimeOffset.Parse("2026-11-07T12:00:00+00:00")
    );

    private readonly LedgerStore _store = TestLedger.Store();
    private readonly WorkingSet _set;
    private readonly PageChoiceStore _choices;
    private readonly PageChoices _service;
    private readonly JobStore _jobs;

    public PageChoicesTests()
    {
        Directory.CreateDirectory(_root);

        _set = new WorkingSet(Path.Combine(_root, "working"));
        _choices = new PageChoiceStore(_store);
        _jobs = new JobStore(Path.Combine(_root, "jobs.jsonl"));
        _service = ServiceOver(new Pages());
    }

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private PageChoices ServiceOver(ISourcePages pages)
    {
        var lookups = new PageLookups(pages, _set, _store, choices: _choices);

        return new PageChoices(
            _choices,
            pages,
            lookups,
            new VerdictByRules(
                _store,
                _set,
                new SourceIndex(_set, Path.Combine(_root, "source-index.json")),
                lookups,
                _time
            ),
            _jobs,
            _time
        );
    }

    private static FetchedPage Page(string site, string url, string key, string? note = null) =>
        new()
        {
            Site = site,
            Url = url,
            Note = note,
            Keys = [new PageKey(key, "Verse", null)],
        };

    private static SiteLookup Hooktheory(FetchedPage[] pages, FetchedPage[]? candidates = null) =>
        new("Hooktheory", LookupStatus.Found, pages) { Candidates = candidates };

    private static SiteLookup NothingOnMusicnotes { get; } =
        new("Musicnotes", LookupStatus.NotFound, []);

    private static Ruling? Rule(IReadOnlyList<SiteLookup> lookups) =>
        VerdictRules.TryRule(Evidence, lookups, out var ruling) ? ruling : null;

    private PageChoice Choice(PageChoiceKind kind, FetchedPage page) =>
        new()
        {
            Url = page.Url,
            Kind = kind,
            Page = page,
            At = _time.GetUtcNow(),
        };

    private void HoldEvidenceAndPages(params SiteLookup[] lookups)
    {
        var folder = _set.Prepare(TrackKey);

        File.WriteAllText(_set.EvidencePathFor(TrackKey), Evidence);
        SiteLookupJsonContext.Default.IReadOnlyListSiteLookup.WriteFile(
            Path.Combine(folder, PageLookups.SourcesFileName),
            lookups
        );
    }

    private void RecordRun(DecidedBy by, DateTimeOffset at) =>
        _store.Record(
            new RunRecord
            {
                RecordedAt = at,
                Song = "artist - song",
                Artist = "Artist",
                Title = "Song",
                SourceUrl = Order.SourceUrl,
                Confidence = KeyConfidence.Medium,
                DecidedBy = by,
                Center = new TonalCenter("D", DiatonicMode.Ionian),
                Evidence = Evidence,
            }
        );

    [Fact]
    public void A_rejected_page_no_longer_counts_toward_tier_A()
    {
        var page = Page("Hooktheory", Tab, "A minor");
        var found = new[] { Hooktheory([page]), NothingOnMusicnotes };

        Assert.Equal(KeyConfidence.High, Rule(found)!.Confidence);

        var effective = PageChoiceEffect.Apply(found, [Choice(PageChoiceKind.Rejected, page)]);
        var ruling = Rule(effective);

        Assert.Equal(KeyConfidence.Medium, ruling!.Confidence);
        Assert.Empty(ruling.Sources);
        Assert.Equal(page, Assert.Single(effective[0].Rejected!));
        Assert.Empty(effective[0].Pages);
    }

    [Fact]
    public void Rejecting_the_page_that_named_another_key_lets_the_other_page_back_a_HIGH()
    {
        var right = Page("Hooktheory", Tab, "A minor");
        var wrong = Page("Hooktheory", Wrong, "C major");
        var found = new[] { Hooktheory([right, wrong]), NothingOnMusicnotes };

        Assert.Null(Rule(found));

        var ruling = Rule(PageChoiceEffect.Apply(found, [Choice(PageChoiceKind.Rejected, wrong)]));

        Assert.Equal(KeyConfidence.High, ruling!.Confidence);
        Assert.Equal(Tab, Assert.Single(ruling.Sources).Url);
    }

    [Fact]
    public void A_promoted_candidate_counts_and_is_no_longer_a_candidate()
    {
        var candidate = Page("Hooktheory", Candidate, "A minor", "unconfirmed: filed under X");
        var found = new[] { Hooktheory([], [candidate]), NothingOnMusicnotes };

        Assert.Equal(KeyConfidence.Medium, Rule(found)!.Confidence);

        var effective = PageChoiceEffect.Apply(found, [Choice(PageChoiceKind.Promoted, candidate)]);
        var ruling = Rule(effective);

        Assert.Equal(KeyConfidence.High, ruling!.Confidence);
        Assert.Equal("counted at your say", Assert.Single(ruling.Sources).Via);
        Assert.Null(effective[0].Candidates);
        Assert.Equal(PageChoiceKind.Promoted, Assert.Single(effective[0].Pages).Chosen);
    }

    [Fact]
    public async Task An_added_Hooktheory_address_is_read_as_a_found_page_is_and_counts()
    {
        var pages = SourcesServing(
            new()
            {
                ["www.hooktheory.com/theorytab/view/billy-joel/piano-man"] = Fixture(
                    "hooktheory-piano-man.html"
                ),
            }
        );

        var fetch = await pages.FetchPageAsync(
            "http://hooktheory.com/theorytab/view/billy-joel/piano-man/?x=1#verse",
            TestContext.Current.CancellationToken
        );

        var page = Assert.IsType<PageFetch.Found>(fetch).Page;

        Assert.Equal(Tab, page.Url);
        Assert.Equal("Hooktheory", page.Site);
        Assert.NotEmpty(page.Keys);

        var effective = PageChoiceEffect.Apply(
            [Hooktheory([]), NothingOnMusicnotes],
            [Choice(PageChoiceKind.Added, page)]
        );
        var added = Assert.Single(effective[0].Pages);

        Assert.Equal(PageChoiceKind.Added, added.Chosen);
        Assert.Equal(page.Keys, added.Keys);
        Assert.Equal(LookupStatus.Found, effective[0].Status);
    }

    [Fact]
    public async Task An_added_Musicnotes_product_is_read_too()
    {
        var pages = SourcesServing(
            new()
            {
                ["www.musicnotes.com/sheetmusic/nelly/hot-in-herre/MN0103472"] = Fixture(
                    "musicnotes-hot-in-herre.html"
                ),
            }
        );

        var fetch = await pages.FetchPageAsync(
            "https://www.musicnotes.com/sheetmusic/nelly/hot-in-herre/MN0103472",
            TestContext.Current.CancellationToken
        );

        var page = Assert.IsType<PageFetch.Found>(fetch).Page;

        Assert.Equal("Musicnotes", page.Site);
        Assert.Equal("E Minor", Assert.Single(page.Keys).Key);
    }

    [Theory]
    [InlineData("https://tabs.ultimate-guitar.com/tab/a/b-1", "never counted")]
    [InlineData("https://example.com/theorytab/view/a/b", "Only a Hooktheory")]
    [InlineData("https://www.hooktheory.com/songs/a", "Only a Hooktheory")]
    [InlineData(
        "https://evil.example/?u=https://www.hooktheory.com/theorytab/view/a/b",
        "Only a Hooktheory"
    )]
    [InlineData("not an address", "Only a Hooktheory")]
    public async Task A_page_that_is_not_a_TheoryTab_or_a_Musicnotes_product_is_refused_without_a_request(
        string address,
        string expected
    )
    {
        var handler = new Serves([]);
        var pages = new SourcePages(new FixedHttpClientFactory(new HttpClient(handler)));

        var fetch = await pages.FetchPageAsync(address, TestContext.Current.CancellationToken);

        Assert.Contains(expected, Assert.IsType<PageFetch.Refused>(fetch).Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_refused_address_makes_no_choice()
    {
        var service = ServiceOver(SourcesServing([]));

        var outcome = await service.AddAsync(
            Order,
            "https://tabs.ultimate-guitar.com/tab/a/b-1",
            TestContext.Current.CancellationToken
        );

        Assert.IsType<PageChoiceOutcome.Refused>(outcome);
        Assert.Empty(_choices.ActiveFor(TrackKey));
    }

    [Fact]
    public async Task Choices_survive_a_fresh_lookup_a_restart_and_a_swapped_source()
    {
        var kept = Page("Hooktheory", Candidate, "A minor");
        var rejected = Page("Hooktheory", Tab, "C major");

        _choices.Choose(TrackKey, Choice(PageChoiceKind.Promoted, kept));
        _choices.Choose(TrackKey, Choice(PageChoiceKind.Rejected, rejected));

        // No kept lookup: a fresh one finds the wrong page again and nothing else.
        var swapped = Order with
        {
            SourceUrl = "https://music.youtube.com/watch?v=OtherUpload1",
        };
        var restarted = new PageLookups(
            new Pages(Hooktheory([rejected]), NothingOnMusicnotes),
            _set,
            _store,
            choices: new PageChoiceStore(_store)
        );
        var found = await restarted.LookUpAsync(swapped, TestContext.Current.CancellationToken);
        var effective = restarted.Effective(TrackKey, found);

        Assert.Equal(Tab, Assert.Single(found[0].Pages).Url);
        Assert.Equal(Candidate, Assert.Single(effective[0].Pages).Url);
        Assert.Equal(Tab, Assert.Single(effective[0].Rejected!).Url);
    }

    [Fact]
    public void An_undone_choice_stops_applying_and_counts_as_a_change_after_the_verdict()
    {
        var page = Page("Hooktheory", Tab, "A minor");
        var verdictAt = _time.GetUtcNow();

        _time.Advance(TimeSpan.FromMinutes(1));
        _choices.Choose(TrackKey, Choice(PageChoiceKind.Rejected, page));
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(_choices.Undo(TrackKey, Tab, _time.GetUtcNow()));

        Assert.Empty(_choices.ActiveFor(TrackKey));
        Assert.True(_service.ChangedSince(TrackKey, verdictAt));
        Assert.False(_service.ChangedSince(TrackKey, _time.GetUtcNow()));
    }

    [Fact]
    public async Task A_rules_verdict_is_ruled_again_on_the_corrected_pages_and_the_old_run_stays()
    {
        var wrong = Page("Hooktheory", Wrong, "C major");

        HoldEvidenceAndPages(Hooktheory([wrong]), NothingOnMusicnotes);
        RecordRun(DecidedBy.Rules, _time.GetUtcNow());
        _time.Advance(TimeSpan.FromMinutes(5));

        var outcome = await _service.RejectAsync(
            Order,
            wrong,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<PageChoiceOutcome.Ruled>(outcome);
        Assert.True(_store.Read().TryGetLatestByTrackKey(TrackKey, out var latest));
        Assert.Equal(DecidedBy.Rules, latest.DecidedBy);
        Assert.Equal("A minor", latest.Center!.Collapsed);
        Assert.Equal(_time.GetUtcNow(), latest.Timestamp);
        Assert.Equal(2, _store.Read().Runs.Count);
    }

    [Fact]
    public async Task A_judges_verdict_is_left_alone()
    {
        var wrong = Page("Hooktheory", Wrong, "C major");

        HoldEvidenceAndPages(Hooktheory([wrong]), NothingOnMusicnotes);
        RecordRun(DecidedBy.Judge, _time.GetUtcNow());

        var verdictAt = _time.GetUtcNow();

        _time.Advance(TimeSpan.FromMinutes(5));

        var outcome = await _service.RejectAsync(
            Order,
            wrong,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new PageChoiceOutcome.Kept(DecidedBy.Judge), outcome);
        Assert.True(_store.Read().TryGetLatestByTrackKey(TrackKey, out var latest));
        Assert.Equal(verdictAt, latest.Timestamp);
        Assert.Single(_store.Read().Runs);
        Assert.True(_service.ChangedSince(TrackKey, verdictAt));
    }

    [Fact]
    public async Task A_track_with_no_verdict_is_ruled_at_once()
    {
        var wrong = Page("Hooktheory", Wrong, "C major");

        HoldEvidenceAndPages(Hooktheory([wrong]), NothingOnMusicnotes);
        _jobs.Record(
            TrackKey,
            JobStage.Verdict,
            JobStatus.AwaitingPerson,
            summary: "waiting for your call"
        );

        var outcome = await _service.RejectAsync(
            Order,
            wrong,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<PageChoiceOutcome.Ruled>(outcome);
        Assert.True(_store.Read().TryGetLatestByTrackKey(TrackKey, out var latest));
        Assert.Equal(DecidedBy.Rules, latest.DecidedBy);
        Assert.True(_jobs.TryGet(TrackKey, JobStage.Verdict, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void The_judge_is_told_what_the_person_rejected_and_added()
    {
        var rejected = Page("Hooktheory", Wrong, "C major");
        var added = Page("Hooktheory", Tab, "A minor");
        var effective = PageChoiceEffect.Apply(
            [Hooktheory([rejected]), NothingOnMusicnotes],
            [Choice(PageChoiceKind.Rejected, rejected), Choice(PageChoiceKind.Added, added)]
        );

        var lines = string.Join('\n', JudgePrompt.PagesLines(effective));

        Assert.Contains(
            $"the person says this is not this song, so it is left out: Hooktheory, {Wrong}",
            lines
        );
        Assert.Contains("the person added this page: it is this song", lines);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SourcePages", name));

    private static SourcePages SourcesServing(Dictionary<string, string?> pages) =>
        new(new FixedHttpClientFactory(new HttpClient(new Serves(pages))));

    private sealed class Pages(params SiteLookup[] lookups) : ISourcePages
    {
        public Task<IReadOnlyList<SiteLookup>> FetchAsync(
            string artist,
            string title,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SiteLookup>>(lookups);
    }

    private sealed class Serves(Dictionary<string, string?> pages) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var uri = request.RequestUri!;

            Requests.Add(uri);

            var page = pages
                .FirstOrDefault(entry =>
                    $"{uri.Host}{uri.AbsolutePath}".StartsWith(entry.Key, StringComparison.Ordinal)
                )
                .Value;

            return Task.FromResult(
                page is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(page),
                    }
            );
        }
    }
}
