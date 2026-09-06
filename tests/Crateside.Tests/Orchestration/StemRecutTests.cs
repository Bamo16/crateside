using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Cutting a track's stems again to a different recipe, without judging it again.
/// </summary>
/// <remarks>
/// The case is a fact discovered late: a release marked as having no vocals turns out to have
/// them, and the mistake only surfaces during warping. What matters most here is what it refuses
/// to do — spend quota, or leave a half-emptied staging folder behind an eligible separation.
/// </remarks>
public class StemRecutTests : IDisposable
{
    private const string Video = "aaaaaaaaaaa";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly string _staging;
    private readonly string _working;

    public StemRecutTests()
    {
        _staging = Path.Combine(_root, "Sample Staging");
        _working = Path.Combine(_root, "working");

        Directory.CreateDirectory(_staging);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private JobStore Jobs() => new(Path.Combine(_root, "jobs.jsonl"));

    private QueueSettingsStore Settings() =>
        new(Options.Create(new QueueOptions()), Path.Combine(_root, "settings.json"));

    /// <summary>A working set holding source audio for the track, as one still in staging has.</summary>
    private WorkingSet WorkingSetWithSource()
    {
        var set = new WorkingSet(_working);
        var folder = set.Prepare(Video);

        File.WriteAllText(Path.Combine(folder, "Rob Dougan - Furious Angels.opus"), "audio");

        return set;
    }

    private readonly List<StagingStem> _staged = [];

    /// <summary>The staged files, as the board would report them. Read as a delegate, so no
    /// audio has to be real for this to be exercised.</summary>
    private ReadStagedFiles Staged() => _ => _staged;

    private string Stage(string name)
    {
        var path = Path.Combine(_staging, name);

        File.WriteAllText(path, "stem");
        _staged.Add(new StagingStem { Path = path, FileName = name });

        return path;
    }

    [Fact]
    public void It_refuses_when_nothing_of_the_track_is_staged()
    {
        var recut = new StemRecut(Settings(), Jobs(), WorkingSetWithSource(), Staged());

        var plan = recut.PlanFor(Video, isInstrumentalRelease: false);

        Assert.False(plan.IsPossible);
        Assert.Contains("Sample Staging", plan.Refusal);
    }

    [Fact]
    public void It_refuses_when_the_downloaded_audio_has_gone()
    {
        // Asked of the working set rather than inferred from staging. The janitor keeps a track's
        // audio while anything of it is staged, so the inference is usually right -- but the
        // working set is cache and is safe to delete by hand, so the only honest way is to look.
        Stage("Rob Dougan - Furious Angels.flac");

        var recut = new StemRecut(Settings(), Jobs(), new WorkingSet(_working), Staged());

        var plan = recut.PlanFor(Video, isInstrumentalRelease: false);

        Assert.False(plan.IsPossible);
        Assert.Contains("no longer on disk", plan.Refusal);
    }

    [Fact]
    public void The_plan_is_the_recipe_the_corrected_fact_calls_for()
    {
        var recut = new StemRecut(Settings(), Jobs(), WorkingSetWithSource(), Staged());

        Assert.Equal(
            SeparationRecipe.Standard,
            recut.PlanFor(Video, isInstrumentalRelease: false).Recipe
        );
        Assert.Equal(
            SeparationRecipe.InstrumentalRelease,
            recut.PlanFor(Video, isInstrumentalRelease: true).Recipe
        );
    }

    [Fact]
    public void It_clears_the_staged_files_and_records_the_corrected_fact()
    {
        // The whole of the reported case: marked as having no vocals, separated to a kept source
        // plus a guide, and the vocals turn out to be there after all.
        var source = Stage("Rob Dougan - Furious Angels.flac");
        var guide = Stage("Rob Dougan - Furious Angels (Drums).flac");

        var settings = Settings();
        var jobs = Jobs();
        var recut = new StemRecut(settings, jobs, WorkingSetWithSource(), Staged());

        settings.SetInstrumentalRelease(Video, true);

        var outcome = recut.Apply(Video, isInstrumentalRelease: false);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Removed);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(guide));

        // The instruction is dropped rather than written as false, because absence already means
        // "not stated" and crateside's policy for not-stated is the usual pair. What matters is the
        // recipe the fact now derives, so that is what is asserted.
        var stated =
            settings
                .Read()
                .Tracks.FirstOrDefault(track => track.VideoId == Video)
                ?.IsInstrumentalRelease
            is true;

        Assert.Equal(SeparationRecipe.Standard, SeparationRecipe.For(stated));
    }

    [Theory]
    [InlineData(JobStage.Separation)]
    [InlineData(JobStage.Handoff)]
    [InlineData(JobStage.Tagging)]
    public void It_reopens_the_stages_that_have_to_run_again(JobStage stage)
    {
        Stage("Rob Dougan - Furious Angels.flac");

        var jobs = Jobs();
        var recut = new StemRecut(Settings(), jobs, WorkingSetWithSource(), Staged());

        jobs.Record(Video, stage, JobStatus.Succeeded);

        recut.Apply(Video, isInstrumentalRelease: false);

        Assert.True(jobs.TryGet(Video, stage, out var job));
        Assert.Equal(JobStatus.Cancelled, job.Status);
    }

    [Theory]
    [InlineData(JobStage.Evidence)]
    [InlineData(JobStage.Verdict)]
    public void It_leaves_the_stages_that_cost_quota_alone(JobStage stage)
    {
        // The structural guard is elsewhere and is the better one: once the staged files are gone
        // the track has a verdict and no stems, which puts it in AwaitingSeparation and therefore
        // out of Outstanding -- the only list evidence and judging read. This is the belt.
        Stage("Rob Dougan - Furious Angels.flac");

        var jobs = Jobs();
        var recut = new StemRecut(Settings(), jobs, WorkingSetWithSource(), Staged());

        jobs.Record(Video, stage, JobStatus.Succeeded);

        recut.Apply(Video, isInstrumentalRelease: false);

        Assert.True(jobs.TryGet(Video, stage, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void A_file_it_cannot_delete_stops_it_before_the_stages_reopen()
    {
        // Nearly always a file Ableton has open. Reopening separation over a folder that still
        // holds some of the old files would separate into the mess rather than replacing it, so
        // the stages stay shut and the user closes Ableton and presses again.
        Stage("Rob Dougan - Furious Angels.flac");

        var held = Stage("Rob Dougan - Furious Angels (Drums).flac");
        var jobs = Jobs();
        var recut = new StemRecut(Settings(), jobs, WorkingSetWithSource(), Staged());

        jobs.Record(Video, JobStage.Separation, JobStatus.Succeeded);

        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = recut.Apply(Video, isInstrumentalRelease: false);

            Assert.False(outcome.Succeeded);
            Assert.Contains("open in Ableton", outcome.Problem);
        }

        Assert.True(jobs.TryGet(Video, JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }
}

/// <summary>
/// How long the GPU will be busy, which is the number the confirmation has to quote.
/// </summary>
public class SeparationEstimateTests
{
    [Fact]
    public void The_estimate_matches_the_two_separations_anyone_timed()
    {
        // 2:50 for one pass and 5:59 for three are the only measurements there are, and the
        // estimate is a straight line through them -- which two points cannot disagree with.
        Assert.Equal(170, SeparationRecipe.InstrumentalRelease.Estimate.TotalSeconds);
        Assert.Equal(360, SeparationRecipe.Standard.Estimate.TotalSeconds);
    }

    [Fact]
    public void A_kept_source_is_not_a_pass()
    {
        // It is copied, not separated, so it costs no GPU at all.
        Assert.Equal(1, SeparationRecipe.InstrumentalRelease.Passes);
        Assert.Equal(2, SeparationRecipe.InstrumentalRelease.ExpectedOutputs);
    }
}
