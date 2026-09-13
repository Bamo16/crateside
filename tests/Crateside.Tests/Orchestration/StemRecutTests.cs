using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Changing which files a track has, without judging it again.
/// </summary>
/// <remarks>
/// The case is a plan corrected late: a track cut without a vocal stem turns out to need one,
/// and the mistake only surfaces during warping. What matters most here is what it refuses
/// to do — spend quota, or leave a half-emptied staging folder behind an eligible separation.
/// </remarks>
public class StemRecutTests : IDisposable
{
    private const string Video = "aaaaaaaaaaa";

    private static readonly PresetCatalog Catalog = PresetCatalogReader.Parse(
        """
        [
          {"id":"vocal_full","displayName":"Vocal - Full","category":"Vocals"},
          {"id":"instrumental_full","displayName":"Instrumental - Full","category":"Instrumentals"},
          {"id":"instrumental_clean","displayName":"Instrumental - Clean","category":"Instrumentals"}
        ]
        """
    );

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

    /// <summary>Stages one file, named and tagged the way StemForge writes it.</summary>
    private string Stage(string preset = "")
    {
        var name = preset is { Length: > 0 }
            ? $"Rob Dougan - Furious Angels ({preset}).flac"
            : "Rob Dougan - Furious Angels.flac";

        var path = Path.Combine(_staging, name);

        File.WriteAllText(path, "stem");
        _staged.Add(
            new StagingStem
            {
                Path = path,
                FileName = name,
                Preset = preset is { Length: > 0 } ? preset : null,
            }
        );

        return path;
    }

    private StemRecut Recut(JobStore? jobs = null, QueueSettingsStore? settings = null) =>
        new(settings ?? Settings(), jobs ?? Jobs(), WorkingSetWithSource(), Staged());

    [Fact]
    public void It_refuses_when_nothing_of_the_track_is_staged()
    {
        var plan = Recut().PlanFor(Video, StemPlan.Default, Catalog);

        Assert.False(plan.IsPossible);
        Assert.Contains("Sample Staging", plan.Refusal);
    }

    [Fact]
    public void It_refuses_when_the_separator_could_not_say_what_these_files_are()
    {
        // Without the catalog every stem reads as one the plan cannot name, so the whole plan
        // looks outstanding — and cutting it would make a second copy of every file this track
        // already has, under a suffixed name.
        Stage("Vocal - Full");

        var plan = Recut().PlanFor(Video, StemPlan.Default, PresetCatalog.Empty);

        Assert.False(plan.IsPossible);
        Assert.Contains("could not be asked", plan.Refusal);
    }

    [Fact]
    public void It_refuses_a_plan_that_asks_for_nothing()
    {
        Stage("Vocal - Full");

        var plan = Recut().PlanFor(Video, new StemPlan { Presets = [] }, Catalog);

        Assert.False(plan.IsPossible);
        Assert.Contains("keep something", plan.Refusal);
    }

    [Fact]
    public void It_refuses_when_the_track_already_has_exactly_these_files()
    {
        Stage("Vocal - Full");
        Stage("Instrumental - Full");

        var plan = Recut().PlanFor(Video, StemPlan.Default, Catalog);

        Assert.False(plan.IsPossible);
        Assert.Contains("already has exactly these files", plan.Refusal);
    }

    [Fact]
    public void A_track_whose_audio_has_gone_is_re_cut_by_fetching_it_again()
    {
        // Asked of the working set rather than inferred from staging. The janitor keeps a track's
        // audio while anything of it is staged, so the inference is usually right -- but the
        // working set is cache and is safe to delete by hand, so the only honest way is to look.
        Stage();

        var recut = new StemRecut(Settings(), Jobs(), new WorkingSet(_working), Staged());

        var plan = recut.PlanFor(Video, StemPlan.Default, Catalog);

        Assert.True(plan.IsPossible);
        Assert.True(plan.WillRefetch);
        Assert.Contains("fetches it again", plan.AudioDetail);
    }

    [Fact]
    public void A_track_whose_audio_is_still_held_says_nothing_will_be_fetched()
    {
        Stage();

        var plan = Recut().PlanFor(Video, StemPlan.Default, Catalog);

        Assert.False(plan.WillRefetch);
        Assert.Contains("still on disk", plan.AudioDetail);
    }

    [Fact]
    public void The_recipe_is_what_the_user_asked_for_minus_what_is_already_there()
    {
        var recut = Recut();

        Assert.Equal(
            SeparationRecipe.Standard,
            recut.PlanFor(Video, StemPlan.Default, Catalog).Recipe
        );

        Stage("Vocal - Full");

        Assert.Equal(
            ["instrumental_full"],
            recut.PlanFor(Video, StemPlan.Default, Catalog).Recipe.Presets
        );
    }

    [Fact]
    public void Adding_a_preset_removes_nothing_and_leaves_the_guide_alone()
    {
        // The common case, and the reason any of this exists: the defaults did not isolate
        // cleanly, so a third preset is wanted and the two already staged are worth keeping.
        var vocal = Stage("Vocal - Full");
        var instrumental = Stage("Instrumental - Full");
        var guide = Stage("Drums - htdemucs_ft");

        var recut = Recut();
        var wanted = new StemPlan
        {
            Presets = ["vocal_full", "instrumental_full", "instrumental_clean"],
        };

        var plan = recut.PlanFor(Video, wanted, Catalog);

        Assert.True(plan.IsAdditive);
        Assert.Equal(["instrumental_clean"], plan.Recipe.Presets);
        Assert.False(plan.Recipe.ExtractDrums);

        Assert.Equal(0, recut.Apply(Video, wanted, Catalog).Removed);
        Assert.True(File.Exists(vocal));
        Assert.True(File.Exists(instrumental));
        Assert.True(File.Exists(guide));
    }

    [Fact]
    public void Dropping_a_preset_removes_only_that_file_and_records_the_new_plan()
    {
        var vocal = Stage("Vocal - Full");
        var instrumental = Stage("Instrumental - Full");
        var guide = Stage("Drums - htdemucs_ft");

        var settings = Settings();
        var wanted = new StemPlan { Presets = ["instrumental_full"] };

        var outcome = Recut(settings: settings).Apply(Video, wanted, Catalog);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, outcome.Removed);
        Assert.False(File.Exists(vocal));
        Assert.True(File.Exists(instrumental));

        // The guide is used up rather than kept, so it is nobody's to drop — least of all a
        // plan that was never able to name it.
        Assert.True(File.Exists(guide));

        var stored = settings.Read().Tracks.Single(track => track.VideoId == Video).Plan;

        Assert.NotNull(stored);
        Assert.True(stored.AsksTheSameAs(wanted));
    }

    [Fact]
    public void An_instrumental_release_that_turns_out_to_have_vocals_loses_its_source()
    {
        // The originally reported case: marked as having no vocals, separated to a kept source
        // plus a guide, and the vocals turn out to be there after all.
        var source = Stage();
        var guide = Stage("Drums - htdemucs_ft");

        var outcome = Recut().Apply(Video, StemPlan.Default, Catalog);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, outcome.Removed);
        Assert.False(File.Exists(source));

        // Cut from the same audio, so it is still the right guide for the stems replacing it.
        Assert.True(File.Exists(guide));
    }

    [Fact]
    public void It_clears_Abletons_analysis_files_along_with_the_audio()
    {
        // Separation puts a file back under the same name, so a sidecar left behind is picked up
        // by new audio: Ableton warps it to the old markers rather than analysing it.
        var source = Stage();

        File.WriteAllText(source + ".asd", "warp markers");

        Assert.True(Recut().Apply(Video, StemPlan.Default, Catalog).Succeeded);
        Assert.False(File.Exists(source + ".asd"));
    }

    [Fact]
    public void A_stem_nobody_analysed_is_removed_without_complaint()
    {
        Stage();

        Assert.True(Recut().Apply(Video, StemPlan.Default, Catalog).Succeeded);
    }

    [Theory]
    [InlineData(JobStage.Separation)]
    [InlineData(JobStage.Handoff)]
    [InlineData(JobStage.Tagging)]
    public void It_reopens_the_stages_that_have_to_run_again(JobStage stage)
    {
        Stage();

        var jobs = Jobs();

        jobs.Record(Video, stage, JobStatus.Succeeded);

        Recut(jobs).Apply(Video, StemPlan.Default, Catalog);

        Assert.True(jobs.TryGet(Video, stage, out var job));
        Assert.Equal(JobStatus.Cancelled, job.Status);
    }

    [Theory]
    [InlineData(JobStage.Evidence)]
    [InlineData(JobStage.Verdict)]
    public void It_leaves_the_stages_that_cost_quota_alone(JobStage stage)
    {
        // The structural guard is elsewhere and is the better one: a re-cut track is in
        // AwaitingSeparation and therefore out of Outstanding -- the only list evidence and
        // judging read. This is the belt.
        Stage();

        var jobs = Jobs();

        jobs.Record(Video, stage, JobStatus.Succeeded);

        Recut(jobs).Apply(Video, StemPlan.Default, Catalog);

        Assert.True(jobs.TryGet(Video, stage, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void A_change_that_only_drops_files_never_reaches_the_GPU()
    {
        // Nothing is made and nothing is moved, so reopening separation would put the track in
        // line for a separator with nothing to do for it.
        Stage("Vocal - Full");
        Stage("Instrumental - Full");

        var jobs = Jobs();

        jobs.Record(Video, JobStage.Separation, JobStatus.Succeeded);

        var outcome = Recut(jobs)
            .Apply(Video, new StemPlan { Presets = ["instrumental_full"] }, Catalog);

        Assert.Equal(1, outcome.Removed);
        Assert.True(jobs.TryGet(Video, JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void A_file_it_cannot_delete_stops_it_before_the_stages_reopen()
    {
        // Nearly always a file Ableton has open. Reopening separation over a folder that still
        // holds a file the plan dropped would separate into the mess rather than replacing it,
        // so the stages stay shut and the user closes Ableton and presses again.
        Stage("Instrumental - Full");

        var held = Stage("Vocal - Full");
        var jobs = Jobs();
        var recut = Recut(jobs);

        jobs.Record(Video, JobStage.Separation, JobStatus.Succeeded);

        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = recut.Apply(
                Video,
                new StemPlan { Presets = ["instrumental_full", "instrumental_clean"] },
                Catalog
            );

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
        Assert.Equal(1, SeparationRecipe.InstrumentalRelease.ExpectedOutputs);
    }
}
