using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Pairing;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class PersonsKeyTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private const string Evidence = """
        {
          "algorithms": {
            "ffmpeg_keydetect": { "key": "A minor", "r": 0.7, "key_alt": "C major" },
            "madmom": { "key": "A minor" },
            "essentia_edma": { "key": "D major" }
          }
        }
        """;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(
        DateTimeOffset.Parse("2026-11-03T12:00:00+00:00")
    );

    private readonly LedgerStore _store = TestLedger.Store();
    private readonly WorkingSet _set;
    private readonly JobStore _jobs;
    private readonly PersonsKey _person;

    private static readonly WorkOrder Order = new()
    {
        VideoId = VideoId,
        Title = "Artist - Song",
        SourceUrl = $"https://music.youtube.com/watch?v={VideoId}",
        Destination = StemDestination.UnresolvedKey,
    };

    public PersonsKeyTests()
    {
        Directory.CreateDirectory(_root);

        _set = new WorkingSet(Path.Combine(_root, "working"));
        _jobs = new JobStore(Path.Combine(_root, "jobs.jsonl"));

        _person = new PersonsKey(
            _store,
            _set,
            new SourceIndex(_set, Path.Combine(_root, "source-index.json")),
            new PageLookups(new NoPages(), _set, _store),
            _jobs,
            _time
        );
    }

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private void HoldEvidenceAndPages()
    {
        var folder = _set.Prepare(VideoId);

        File.WriteAllText(_set.EvidencePathFor(VideoId), Evidence);
        File.WriteAllText(
            Path.Combine(folder, PageLookups.SourcesFileName),
            """
            [{"site":"Hooktheory","status":"Found","pages":[{"site":"Hooktheory","url":"https://www.hooktheory.com/theorytab/view/a/b","keys":[{"key":"C major","section":"Verse"},{"key":"B dorian","section":"Chorus"}]}]},
             {"site":"Ultimate Guitar","status":"Found","pages":[{"site":"Ultimate Guitar","url":"https://tabs.ultimate-guitar.com/tab/a/b-1","keys":[{"key":"F major"}]}]}]
            """
        );
    }

    [Fact]
    public void A_track_nothing_has_ruled_on_offers_every_key_its_evidence_names_with_who_named_it()
    {
        HoldEvidenceAndPages();

        var choices = _person.ChoicesFor(Order, run: null);

        Assert.Equal(
            ["A minor", "C major", "D major", "B minor"],
            choices.Select(choice => choice.Key.LedgerName)
        );
        Assert.Equal(["keydetect", "madmom"], choices[0].NamedBy);
        Assert.Equal(["keydetect's runner-up", "Hooktheory page"], choices[1].NamedBy);
        Assert.Equal(["Essentia"], choices[2].NamedBy);
        Assert.Equal(["Hooktheory page"], choices[3].NamedBy);
    }

    [Fact]
    public void Ultimate_Guitar_names_no_choice()
    {
        HoldEvidenceAndPages();

        Assert.DoesNotContain(
            _person.ChoicesFor(Order, run: null),
            choice => choice.Key.LedgerName == "F major"
        );
    }

    [Fact]
    public void A_track_with_no_evidence_offers_nothing()
    {
        Assert.Empty(_person.ChoicesFor(Order, run: null));
    }

    [Fact]
    public void A_track_that_was_waiting_takes_the_choice_as_a_HIGH_verdict_of_the_person()
    {
        HoldEvidenceAndPages();
        _jobs.Record(VideoId, JobStage.Verdict, JobStatus.AwaitingPerson);

        Assert.IsType<RecordOutcome.Recorded>(
            _person.Choose(Order, MusicKey.FromString("A minor")!)
        );

        var run = Assert.Single(_store.Read().Runs);

        Assert.Equal(DecidedBy.Person, run.DecidedBy);
        Assert.Equal(KeyConfidence.High, run.Confidence);
        Assert.Equal("A minor", run.Key);
        Assert.Equal(new TonalCenter("A", DiatonicMode.Aeolian), run.Center);
        Assert.Equal("artist - song", run.Song);
        Assert.True(run.HasTaggableKey);

        var evidence = _store.EvidenceFor("artist - song")!;
        var ear = Assert.Single(evidence.Sources);

        Assert.Equal(SourceKind.Ear, ear.Kind);
        Assert.Equal("A minor", ear.StatesKey);
        Assert.Equal(3, evidence.Algorithms.Count);
        Assert.Contains(
            evidence.Lookups,
            lookup => lookup is { Site: "Hooktheory", Pages.Count: 1 }
        );
        Assert.True(_jobs.TryGet(VideoId, JobStage.Verdict, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void A_choice_of_a_flat_minor_key_is_stored_on_the_sharp_tonic()
    {
        HoldEvidenceAndPages();

        _person.Choose(Order, MusicKey.FromString("Eb minor")!);

        Assert.Equal(
            new TonalCenter("D#", DiatonicMode.Aeolian),
            Assert.Single(_store.Read().Runs).Center
        );
    }

    [Fact]
    public void A_choice_over_a_withheld_verdict_supersedes_it_and_keeps_the_song()
    {
        _store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                Song = "the artist - the song",
                Artist = "The Artist",
                Title = "The Song",
                SourceUrl = Order.SourceUrl,
                Confidence = KeyConfidence.Withheld,
                Evidence = Evidence,
            }
        );

        _person.Choose(Order, MusicKey.FromString("D major")!);

        Assert.True(_store.Read().TryGetLatestByVideoId(VideoId, out var latest));
        Assert.Equal("the artist - the song", latest.Song);
        Assert.Equal(DecidedBy.Person, latest.DecidedBy);
        Assert.Equal("D major", latest.Key);
        Assert.Equal(2, _store.Read().Runs.Count);
        Assert.Equal(3, _store.EvidenceFor("the artist - the song")!.Algorithms.Count);
    }

    [Fact]
    public void A_choice_over_a_judged_key_replaces_it_whatever_the_judge_said()
    {
        _store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                Song = "artist - song",
                Artist = "Artist",
                Title = "Song",
                SourceUrl = Order.SourceUrl,
                Confidence = KeyConfidence.Medium,
                Center = new TonalCenter("C", DiatonicMode.Ionian),
            }
        );

        _person.Choose(Order, MusicKey.FromString("A minor")!);

        Assert.True(_store.Read().TryGetLatestByVideoId(VideoId, out var latest));
        Assert.Equal("A minor", latest.Key);
        Assert.Equal(KeyConfidence.High, latest.Confidence);
    }

    [Fact]
    public void A_parked_stem_is_released_once_the_person_has_chosen()
    {
        var paths = new CratesidePaths
        {
            StemsKeyIdentified = Path.Combine(_root, "identified"),
            StemsUnresolvedKey = Path.Combine(_root, "unresolved"),
            StagingFolder = Path.Combine(_root, "staging"),
        };
        var parked = Path.Combine(paths.StemsUnresolvedKey, "Artist - Song.flac");

        Directory.CreateDirectory(paths.StemsUnresolvedKey);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), parked);

        using (var file = TagLib.File.Create(parked))
        {
            file.Tag.Comment =
                $"stemforge/0.3.1.0 | date: 2026-11-03 | source: https://www.youtube.com/watch?v={VideoId}";
            file.Save();
        }

        HoldEvidenceAndPages();

        Assert.Equal(0, StemLanding.ReleaseParked(paths, _store.Read(), _jobs));

        _person.Choose(Order, MusicKey.FromString("A minor")!);

        Assert.Equal(1, StemLanding.ReleaseParked(paths, _store.Read(), _jobs));
        Assert.True(File.Exists(Path.Combine(paths.StemsKeyIdentified, "Artist - Song.flac")));
        Assert.False(File.Exists(parked));
    }

    private sealed class NoPages : ISourcePages
    {
        public Task<IReadOnlyList<SiteLookup>> FetchAsync(
            string artist,
            string title,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SiteLookup>>([]);
    }
}
