using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration;

public sealed class WorkingSetJanitorTests : IDisposable
{
    private const string TrackKey = "dQw4w9WgXcQ";

    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string Staging => Path.Combine(_directory, "Staging");
    private string Library => Path.Combine(_directory, "Library");
    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    private readonly FakeTimeProvider _time = new();

    private SourceRetentionStore Retention =>
        field ??= new SourceRetentionStore(Path.Combine(_directory, "retention.json"));

    private TrackInstructionStore Instructions => field ??= new TrackInstructionStore(_ledger);

    private WorkingSetJanitor Janitor =>
        field ??= new WorkingSetJanitor(
            TestLedger.Store(Path.Combine(_directory, "runs.jsonl")),
            Set,
            Jobs,
            Options.Create(new CratesidePaths { StagingFolder = Staging, LibraryFolder = Library }),
            Retention,
            _time,
            Instructions
        );

    public WorkingSetJanitorTests() => Directory.CreateDirectory(Staging);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private void GiveWorkingSet()
    {
        File.WriteAllText(Path.Combine(Set.Prepare(TrackKey), "track.flac"), "audio");
        File.WriteAllText(Set.EvidencePathFor(TrackKey), "{}");
    }

    [Fact]
    public void A_track_still_owed_a_handoff_keeps_its_working_set()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Jobs.Record(TrackKey, JobStage.Handoff, JobStatus.Canceled, error: "re-cut requested");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void A_track_whose_stems_have_left_staging_is_finished_with()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);

        Assert.Equal(1, Janitor.Sweep());
        Assert.Empty(Set.TrackKeys);
    }

    [Fact]
    public void A_track_with_a_stage_running_keeps_its_working_set()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Jobs.Record(TrackKey, JobStage.Verdict, JobStatus.Running);

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
        Assert.True(Set.HasEvidence(TrackKey));
    }

    [Fact]
    public void A_track_waiting_for_the_person_keeps_the_evidence_they_decide_from()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Jobs.Record(TrackKey, JobStage.Verdict, JobStatus.AwaitingPerson);

        Assert.Equal(0, Janitor.Sweep());
        Assert.True(Set.HasEvidence(TrackKey));
    }

    [Fact]
    public void A_track_that_was_never_separated_keeps_its_working_set()
    {
        // "Nothing in staging" is also true before the stems exist.
        GiveWorkingSet();

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void A_track_still_being_worked_in_ableton_keeps_its_working_set()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        WriteStem("Yes - Owner of a Lonely Heart (Vocal - Full).flac");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void A_partly_imported_song_is_not_finished_with()
    {
        // Staging empties progressively, so this has to be "nothing left", not "no samples left".
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        WriteStem("Yes - Owner of a Lonely Heart (Drums).flac");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void A_failed_separation_keeps_its_working_set_so_the_retry_costs_no_download()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Failed, error: "cuda out of memory");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void Sweeping_nothing_is_not_an_error() => Assert.Equal(0, Janitor.Sweep());

    [Fact]
    public void A_re_cut_asked_for_since_the_separation_keeps_the_audio_it_will_cut_from()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Instructions.RequestCut(TrackKey, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void A_track_queued_only_to_judge_library_files_is_finished_with_once_judged()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem();

        Assert.Equal(1, Janitor.Sweep());
        Assert.Empty(Set.TrackKeys);
    }

    [Fact]
    public void A_judged_track_with_nothing_on_disk_keeps_its_working_set_for_separation()
    {
        GiveWorkingSet();
        Judge();

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void A_source_is_kept_until_its_retention_runs_out_counted_from_when_the_track_left_staging()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Retention.Remember(new SourceRetention { Days = 7 });

        Assert.Equal(0, Janitor.Sweep());
        Assert.Equal(_time.GetUtcNow(), Set.LeftStagingAt(TrackKey));

        _time.Advance(TimeSpan.FromDays(6.9));

        Assert.Equal(0, Janitor.Sweep());

        _time.Advance(TimeSpan.FromDays(0.2));

        Assert.Equal(1, Janitor.Sweep());
        Assert.Empty(Set.TrackKeys);
    }

    [Fact]
    public void Forever_keeps_every_source_however_long_it_has_been()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Retention.Remember(new SourceRetention { Forever = true });

        Janitor.Sweep();
        _time.Advance(TimeSpan.FromDays(3650));

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void Shortening_the_retention_discards_what_is_already_held_on_the_next_pass()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Retention.Remember(new SourceRetention { Forever = true });
        Janitor.Sweep();
        _time.Advance(TimeSpan.FromDays(10));

        Retention.Remember(new SourceRetention { Days = 5 });

        Assert.Equal(1, Janitor.Sweep());
    }

    [Fact]
    public void A_track_whose_stems_come_back_to_staging_starts_its_clock_again()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Retention.Remember(new SourceRetention { Days = 7 });
        Janitor.Sweep();
        WriteStem("Yes - Owner of a Lonely Heart (Vocal - Full).flac");

        Janitor.Sweep();

        Assert.Null(Set.LeftStagingAt(TrackKey));
    }

    [Fact]
    public void A_source_the_library_already_keeps_as_a_sample_is_discarded_whatever_the_retention()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Retention.Remember(new SourceRetention { Forever = true });
        WriteLibraryStem("Yes - Owner of a Lonely Heart.flac", preset: null);

        Assert.Equal(1, Janitor.Sweep());
        Assert.Empty(Set.TrackKeys);
    }

    [Fact]
    public void A_stem_in_the_library_is_not_a_kept_source()
    {
        GiveWorkingSet();
        Jobs.Record(TrackKey, JobStage.Separation, JobStatus.Succeeded);
        Retention.Remember(new SourceRetention { Forever = true });
        WriteLibraryStem("Yes - Owner of a Lonely Heart (Vocal - Full).flac", "Vocal - Full");

        Assert.Equal(0, Janitor.Sweep());
    }

    [Fact]
    public void Forever_keeps_a_judging_download_too()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem();
        Retention.Remember(new SourceRetention { Forever = true });

        Janitor.Sweep();
        _time.Advance(TimeSpan.FromDays(3650));

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.TrackKeys);
    }

    [Fact]
    public void A_judging_download_is_kept_for_the_retention_from_when_the_library_files_carry_its_id()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem();
        Retention.Remember(new SourceRetention { Days = 7 });

        Assert.Equal(0, Janitor.Sweep());
        Assert.Equal(_time.GetUtcNow(), Set.LeftStagingAt(TrackKey));

        _time.Advance(TimeSpan.FromDays(7));

        Assert.Equal(1, Janitor.Sweep());
    }

    [Fact]
    public void A_judging_download_the_library_keeps_as_a_sample_is_discarded_whatever_the_retention()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem("Yes - Owner of a Lonely Heart.flac", preset: null);
        Retention.Remember(new SourceRetention { Forever = true });

        Assert.Equal(1, Janitor.Sweep());
    }

    [Fact]
    public void A_download_that_joined_the_store_follows_the_retention_not_the_judging_rule()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem();
        Retention.Remember(new SourceRetention { Days = 30 });
        Set.RecordLeftStaging(TrackKey, _time.GetUtcNow());

        Assert.Equal(0, Janitor.Sweep());

        _time.Advance(TimeSpan.FromDays(31));

        Assert.Equal(1, Janitor.Sweep());
    }

    [Fact]
    public void A_mix_fetched_again_for_playback_is_kept_a_day_even_when_retention_keeps_nothing()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem();
        Retention.Remember(new SourceRetention { Days = 0 });
        Set.RecordLeftStaging(TrackKey, _time.GetUtcNow(), adHoc: true);

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(0, Janitor.Sweep());

        _time.Advance(TimeSpan.FromHours(24));

        Assert.Equal(1, Janitor.Sweep());
    }

    [Fact]
    public void A_pipeline_download_is_still_discarded_the_moment_retention_of_zero_days_says()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem();
        Retention.Remember(new SourceRetention { Days = 0 });
        Set.RecordLeftStaging(TrackKey, _time.GetUtcNow());

        Assert.Equal(1, Janitor.Sweep());
    }

    private void Judge() =>
        File.WriteAllText(
            Path.Combine(_directory, "runs.jsonl"),
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-09-24T00:00:00+00:00",
                    song = "yes - owner of a lonely heart",
                    source_url = $"https://www.youtube.com/watch?v={TrackKey}",
                    verdict = new { key = "A minor", confidence = "HIGH" },
                }
            )
        );

    private void WriteLibraryStem(
        string fileName = "Yes - Owner of a Lonely Heart (Vocal - Full).flac",
        string? preset = "Vocal - Full"
    )
    {
        Directory.CreateDirectory(Library);

        var path = Path.Combine(Library, fileName);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.2.1.1 | {(preset is null ? string.Empty : $"preset: {preset} | ")}source: https://www.youtube.com/watch?v={TrackKey}";
        file.Save();
    }

    private void WriteStem(string fileName)
    {
        var path = Path.Combine(Staging, fileName);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"),
            path,
            overwrite: true
        );

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.2.1.1 | preset: Vocal - Full | date: 2026-08-17 | source: https://www.youtube.com/watch?v={TrackKey}";
        file.Save();
    }
}
