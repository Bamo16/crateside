using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration;

public class WorkingSetJanitorTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string Staging => Path.Combine(_directory, "Sample Staging");
    private string Library => Path.Combine(_directory, "Mashup Samples");
    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    private DateTimeOffset? _requested;

    private WorkingSetJanitor Janitor =>
        field ??= new WorkingSetJanitor(
            Set,
            Jobs,
            Options.Create(
                new CratesidePaths
                {
                    SampleStaging = Staging,
                    MashupSamples = Library,
                    Ledger = Path.Combine(_directory, "runs.jsonl"),
                }
            ),
            _ => _requested
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
        File.WriteAllText(Path.Combine(Set.Prepare(VideoId), "track.flac"), "audio");
        File.WriteAllText(Set.EvidencePathFor(VideoId), "{}");
    }

    [Fact]
    public void A_track_still_owed_a_handoff_keeps_its_working_set()
    {
        GiveWorkingSet();
        Jobs.Record(VideoId, JobStage.Separation, JobStatus.Succeeded);
        Jobs.Record(VideoId, JobStage.Handoff, JobStatus.Cancelled, error: "re-cut requested");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.VideoIds);
    }

    [Fact]
    public void A_track_whose_stems_have_left_staging_is_finished_with()
    {
        GiveWorkingSet();
        Jobs.Record(VideoId, JobStage.Separation, JobStatus.Succeeded);

        Assert.Equal(1, Janitor.Sweep());
        Assert.Empty(Set.VideoIds);
    }

    [Fact]
    public void A_track_that_was_never_separated_keeps_its_working_set()
    {
        // "Nothing in staging" is also true before the stems exist.
        GiveWorkingSet();

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.VideoIds);
    }

    [Fact]
    public void A_track_still_being_worked_in_ableton_keeps_its_working_set()
    {
        GiveWorkingSet();
        Jobs.Record(VideoId, JobStage.Separation, JobStatus.Succeeded);
        WriteStem("Yes - Owner of a Lonely Heart (Vocal - Full).flac");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.VideoIds);
    }

    [Fact]
    public void A_partly_imported_song_is_not_finished_with()
    {
        // Staging empties progressively, so this has to be "nothing left", not "no samples left".
        GiveWorkingSet();
        Jobs.Record(VideoId, JobStage.Separation, JobStatus.Succeeded);
        WriteStem("Yes - Owner of a Lonely Heart (Drums).flac");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.VideoIds);
    }

    [Fact]
    public void A_failed_separation_keeps_its_working_set_so_the_retry_costs_no_download()
    {
        GiveWorkingSet();
        Jobs.Record(VideoId, JobStage.Separation, JobStatus.Failed, error: "cuda out of memory");

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.VideoIds);
    }

    [Fact]
    public void Sweeping_nothing_is_not_an_error() => Assert.Equal(0, Janitor.Sweep());

    [Fact]
    public void A_re_cut_asked_for_since_the_separation_keeps_the_audio_it_will_cut_from()
    {
        GiveWorkingSet();
        Jobs.Record(VideoId, JobStage.Separation, JobStatus.Succeeded);
        _requested = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.VideoIds);
    }

    [Fact]
    public void A_track_queued_only_to_judge_library_files_is_finished_with_once_judged()
    {
        GiveWorkingSet();
        Judge();
        WriteLibraryStem();

        Assert.Equal(1, Janitor.Sweep());
        Assert.Empty(Set.VideoIds);
    }

    [Fact]
    public void A_judged_track_with_nothing_on_disk_keeps_its_working_set_for_separation()
    {
        GiveWorkingSet();
        Judge();

        Assert.Equal(0, Janitor.Sweep());
        Assert.Single(Set.VideoIds);
    }

    private void Judge() =>
        File.WriteAllText(
            Path.Combine(_directory, "runs.jsonl"),
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-09-24T00:00:00+00:00",
                    song = "yes - owner of a lonely heart",
                    source_url = $"https://www.youtube.com/watch?v={VideoId}",
                    verdict = new { key = "A minor", confidence = "HIGH" },
                }
            )
        );

    private void WriteLibraryStem()
    {
        Directory.CreateDirectory(Library);

        var path = Path.Combine(Library, "Yes - Owner of a Lonely Heart (Vocal - Full).flac");

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.2.1.1 | preset: Vocal - Full | source: https://www.youtube.com/watch?v={VideoId}";
        file.Save();
    }

    private void WriteStem(string fileName)
    {
        var source = Directory
            .EnumerateFiles(
                @"P:\RYAN\Ableton\Sample Staging",
                "*.flac",
                SearchOption.TopDirectoryOnly
            )
            .FirstOrDefault();

        Assert.SkipWhen(source is null, "no real staged stem to copy provenance from");

        var path = Path.Combine(Staging, fileName);

        File.Copy(source!, path, overwrite: true);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.2.1.1 | preset: Vocal - Full | date: 2026-08-17 | source: https://www.youtube.com/watch?v={VideoId}";
        file.Save();
    }
}
