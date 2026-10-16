using System.Text.Json;
using Crateside.Core.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Ledger;

public sealed class LedgerStoreTests : IDisposable
{
    private const string Song = "rob dougan - furious angels";
    private const string Judged = "NSLYAT7sjAA";
    private const string Other = "dQw4w9WgXcQ";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(
        DateTimeOffset.Parse("2026-10-15T12:00:00+00:00")
    );

    private LedgerStore Store => field ??= new(LedgerStore.InMemory, _time);

    public LedgerStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Store.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static RunRecord Run(
        string? url = $"https://music.youtube.com/watch?v={Judged}",
        KeyConfidence confidence = KeyConfidence.High,
        TonalCentre? centre = null
    ) =>
        new()
        {
            RecordedAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
            Song = Song,
            Artist = "Rob Dougan",
            Title = "Furious Angels",
            SourceUrl = url,
            Confidence = confidence,
            Centre = centre ?? new TonalCentre("A", DiatonicMode.Dorian),
        };

    [Fact]
    public void A_recorded_run_reads_back_with_its_collapse_computed()
    {
        Assert.IsType<RecordOutcome.Recorded>(Store.Record(Run()));

        Assert.True(Store.Read().TryGetLatestByVideoId(Judged, out var run));
        Assert.Equal("A minor", run.Key);
        Assert.Equal("A Dorian", run.ModalKey);
    }

    [Fact]
    public void A_run_with_no_source_is_refused_unless_it_says_it_is_local()
    {
        var refused = Assert.IsType<RecordOutcome.Refused>(Store.Record(Run(url: null)));

        Assert.Equal("source_url", refused.Field);
        Assert.IsType<RecordOutcome.Recorded>(
            Store.Record(Run(url: null) with { LocalSource = true })
        );
    }

    [Fact]
    public void Only_a_withheld_verdict_may_name_no_centre()
    {
        Assert.Equal(
            "centre",
            Assert.IsType<RecordOutcome.Refused>(Store.Record(Run() with { Centre = null })).Field
        );
        Assert.IsType<RecordOutcome.Recorded>(
            Store.Record(Run(confidence: KeyConfidence.Withheld) with { Centre = null })
        );
    }

    [Fact]
    public void A_name_mis_encoded_on_the_way_in_is_repaired()
    {
        Store.Record(Run() with { Artist = "RÃ¶yksopp" });

        Assert.Equal("Röyksopp", Assert.Single(Store.Read().Runs).Artist);
    }

    [Fact]
    public void A_name_that_cannot_be_repaired_is_refused()
    {
        var refused = Assert.IsType<RecordOutcome.Refused>(
            Store.Record(Run() with { Title = "Furious � Angels" })
        );

        Assert.Equal("title", refused.Field);
        Assert.Empty(Store.Read().Runs);
    }

    [Fact]
    public void A_chord_recogniser_is_filed_as_one_whatever_it_was_called()
    {
        Store.Record(
            Run() with
            {
                Sources =
                [
                    new RunSource
                    {
                        Url = "https://chordify.net/chords/x",
                        Kind = SourceKind.Authoritative,
                        States = "E Mixolydian throughout",
                    },
                ],
            }
        );

        var source = Assert.Single(Store.EvidenceFor(Song)!.Sources);

        Assert.Equal(SourceKind.ChordRecogniser, source.Kind);
        Assert.Equal("E major", source.StatesKey);
        Assert.Equal("mixolydian", source.StatesMode);
    }

    [Fact]
    public void Sections_windows_the_direction_and_the_ear_come_back_as_recorded()
    {
        Store.Record(
            Run() with
            {
                Sections = [new Section("Chorus", new TonalCentre("E", DiatonicMode.Aeolian))],
                Windows = [new RunWindow(53.5, 80.0, "Chorus", WindowAsker.User)],
                Direction = "Judge the choruses; the verses have no harmony.",
                Sources =
                [
                    new RunSource
                    {
                        Kind = SourceKind.Ear,
                        Via = "the maintainer, by ear",
                        StatesKey = "E minor",
                        Sections = ["Chorus"],
                    },
                ],
            }
        );

        var evidence = Store.EvidenceFor(Song)!;

        Assert.Equal("E", Assert.Single(evidence.Sections).Centre.Tonic);
        Assert.Equal(
            new RunWindow(53.5, 80.0, "Chorus", WindowAsker.User),
            Assert.Single(evidence.Windows)
        );
        Assert.Equal("Judge the choruses; the verses have no harmony.", evidence.Direction);
        Assert.Equal(["Chorus"], Assert.Single(evidence.Sources).Sections);
        Assert.Equal(SourceKind.Ear, Assert.Single(evidence.Sources).Kind);
    }

    [Fact]
    public void A_restatement_carries_the_verdict_to_the_new_upload()
    {
        Store.Record(Run());

        Assert.IsType<RestateOutcome.Restated>(Store.Restate(Judged, Other));

        Assert.True(Store.Read().TryGetLatestByVideoId(Other, out var run));
        Assert.Equal("A minor", run.Key);
        Assert.Equal(1, Store.Read().SongCount);
    }

    [Fact]
    public void An_upload_the_song_already_has_is_a_skip_not_a_failure()
    {
        Store.Record(Run());
        Store.Restate(Judged, Other);

        var again = Store.Restate(Judged, Other);

        Assert.IsType<RestateOutcome.AlreadyHeld>(again);
        Assert.Null(again.Refusal);
        Assert.Equal(2, Store.Read().Runs.Count);
    }

    [Fact]
    public void An_upload_the_ledger_has_never_seen_cannot_be_restated_from()
    {
        Assert.NotNull(Store.Restate(Judged, Other).Refusal);
    }

    [Fact]
    public void A_later_run_supersedes_and_the_restated_upload_follows_it()
    {
        Store.Record(Run());
        Store.Restate(Judged, Other);
        Store.Record(
            Run() with
            {
                RecordedAt = DateTimeOffset.Parse("2026-10-20T00:00:00+00:00"),
                Centre = new TonalCentre("C", DiatonicMode.Ionian),
            }
        );

        Assert.True(Store.Read().TryGetLatestByVideoId(Other, out var run));
        Assert.Equal("C major", run.Key);
    }

    [Fact]
    public void Each_start_keeps_a_dated_backup_and_only_the_last_seven()
    {
        using var store = new LedgerStore(Path.Combine(_root, "ledger.db"), _time);

        store.Record(Run());

        for (var i = 0; i < 9; i++)
        {
            store.Start();
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var backups = Directory.GetFiles(store.BackupFolder!, "ledger-*.db");

        Assert.Equal(7, backups.Length);

        using var copy = new LedgerStore(backups.Order().Last(), _time);

        Assert.Single(copy.Read().Runs);
    }

    [Fact]
    public void The_export_has_a_line_for_every_run_and_restatement()
    {
        using var store = new LedgerStore(Path.Combine(_root, "ledger.db"), _time);

        store.Record(Run() with { Evidence = """{"chords":["Am","G"]}""" });
        store.Restate(Judged, Other);

        var lines = File.ReadAllLines(store.ExportPath!)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToList();

        Assert.Equal(2, lines.Count);
        Assert.Equal("Am", lines[0].GetProperty("evidence").GetProperty("chords")[0].GetString());
        Assert.Equal(Song, lines[1].GetProperty("restatement").GetProperty("song").GetString());
    }
}
