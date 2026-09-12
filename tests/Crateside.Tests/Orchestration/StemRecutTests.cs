using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Cutting a track's stems again to a different recipe, without judging it again.
/// </summary>
/// <remarks>
/// The case is a plan corrected late: a track cut without a vocal stem turns out to need one,
/// and the mistake only surfaces during warping. What matters most here is what it refuses
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

        var plan = recut.PlanFor(Video, StemPlan.Default);

        Assert.False(plan.IsPossible);
        Assert.Contains("Sample Staging", plan.Refusal);
    }

    [Fact]
    public void A_track_whose_audio_has_gone_is_re_cut_by_fetching_it_again()
    {
        // Asked of the working set rather than inferred from staging. The janitor keeps a track's
        // audio while anything of it is staged, so the inference is usually right -- but the
        // working set is cache and is safe to delete by hand, so the only honest way is to look.
        Stage("Rob Dougan - Furious Angels.flac");

        var recut = new StemRecut(Settings(), Jobs(), new WorkingSet(_working), Staged());

        var plan = recut.PlanFor(Video, StemPlan.Default);

        Assert.True(plan.IsPossible);
        Assert.True(plan.WillRefetch);
        Assert.Contains("fetches it again", plan.AudioDetail);
    }

    [Fact]
    public void A_track_whose_audio_is_still_held_says_nothing_will_be_fetched()
    {
        Stage("Rob Dougan - Furious Angels.flac");

        var recut = new StemRecut(Settings(), Jobs(), WorkingSetWithSource(), Staged());

        var plan = recut.PlanFor(Video, StemPlan.Default);

        Assert.False(plan.WillRefetch);
        Assert.Contains("still on disk", plan.AudioDetail);
    }

    [Fact]
    public void The_plan_is_the_recipe_the_user_asked_for()
    {
        var recut = new StemRecut(Settings(), Jobs(), WorkingSetWithSource(), Staged());

        Assert.Equal(SeparationRecipe.Standard, recut.PlanFor(Video, StemPlan.Default).Recipe);
        Assert.Equal(
            SeparationRecipe.InstrumentalRelease,
            recut.PlanFor(Video, StemPlan.InstrumentalRelease).Recipe
        );
    }

    [Fact]
    public void It_clears_the_staged_files_and_records_the_new_plan()
    {
        // The whole of the reported case: marked as having no vocals, separated to a kept source
        // plus a guide, and the vocals turn out to be there after all.
        var source = Stage("Rob Dougan - Furious Angels.flac");
        var guide = Stage("Rob Dougan - Furious Angels (Drums).flac");

        var settings = Settings();
        var jobs = Jobs();
        var recut = new StemRecut(settings, jobs, WorkingSetWithSource(), Staged());

        settings.SetPlan(Video, StemPlan.InstrumentalRelease);

        var outcome = recut.Apply(Video, StemPlan.Default);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Removed);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(guide));

        // The plan is now kept rather than dropped. It used to be written as a fact, and "not an
        // instrumental release" was indistinguishable from never having said — so the entry went.
        // A plan is what the user asked for, and asking for the usual pair is a thing they said.
        var stored = settings.Read().Tracks.Single(track => track.VideoId == Video).Plan;

        Assert.NotNull(stored);
        Assert.True(stored.AsksTheSameAs(StemPlan.Default));
    }

    [Fact]
    public void It_clears_Abletons_analysis_files_along_with_the_audio()
    {
        // Separation puts a file back under the same name, so a sidecar left behind is picked up
        // by new audio: Ableton warps it to the old markers rather than analysing it.
        var source = Stage("Rob Dougan - Furious Angels.flac");

        File.WriteAllText(source + ".asd", "warp markers");

        var recut = new StemRecut(Settings(), Jobs(), WorkingSetWithSource(), Staged());

        Assert.True(recut.Apply(Video, StemPlan.Default).Succeeded);
        Assert.False(File.Exists(source + ".asd"));
    }

    [Fact]
    public void A_stem_nobody_analysed_is_removed_without_complaint()
    {
        Stage("Rob Dougan - Furious Angels.flac");

        var recut = new StemRecut(Settings(), Jobs(), WorkingSetWithSource(), Staged());

        Assert.True(recut.Apply(Video, StemPlan.Default).Succeeded);
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

        recut.Apply(Video, StemPlan.Default);

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

        recut.Apply(Video, StemPlan.Default);

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
            var outcome = recut.Apply(Video, StemPlan.Default);

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
