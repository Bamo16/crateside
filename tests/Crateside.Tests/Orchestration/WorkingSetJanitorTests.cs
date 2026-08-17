using Crateside.Core;
using Crateside.Core.Orchestration;
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
    private JobStore Jobs => field ??= new JobStore(Path.Combine(_directory, "jobs.jsonl"));
    private WorkingSet Set => field ??= new WorkingSet(Path.Combine(_directory, "working"));

    private WorkingSetJanitor Janitor =>
        field ??= new WorkingSetJanitor(
            Set,
            Jobs,
            Options.Create(
                new CratesidePaths
                {
                    SampleStaging = Staging,
                    Ledger = Path.Combine(_directory, "runs.jsonl"),
                }
            )
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
        // "Nothing in staging" is also true before the stems exist, so on its own it would
        // delete the source of every track in flight -- including the one being detected.
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
        // The manual workflow empties staging progressively -- the warp guide is deleted
        // mid-warp, the vocal dragged out before the instrumental -- so this has to be
        // "nothing left" rather than "no samples left".
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

    /// <summary>A staged stem carrying this track's video ID in its StemForge provenance.</summary>
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

        // The join is on the video ID StemForge embedded, so the file has to carry ours.
        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.2.1.1 | preset: Vocal - Full | date: 2026-08-17 | source: https://www.youtube.com/watch?v={VideoId}";
        file.Save();
    }
}
