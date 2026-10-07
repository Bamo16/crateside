using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.Staging;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Edits;

/// <summary>Changing which files a track has, without judging it again.</summary>
public sealed class StemRecutTests : IDisposable
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

    private TrackInstructionStore Settings() =>
        new(
            new QueueFile(
                Options.Create(new PlaylistOptions()),
                Path.Combine(_root, "settings.json")
            )
        );

    /// <summary>A working set holding source audio for the track, as one still in staging has.</summary>
    private WorkingSet WorkingSetWithSource()
    {
        var set = new WorkingSet(_working);
        var folder = set.Prepare(Video);

        File.WriteAllText(Path.Combine(folder, "Rob Dougan - Furious Angels.opus"), "audio");

        return set;
    }

    private readonly List<StagedFile> _staged = [];

    private ReadStagedFiles Staged() => _ => _staged;

    private string Stage(string preset = "")
    {
        var name = preset is { Length: > 0 }
            ? $"Rob Dougan - Furious Angels ({preset}).flac"
            : "Rob Dougan - Furious Angels.flac";

        var path = Path.Combine(_staging, name);

        File.WriteAllText(path, "stem");
        _staged.Add(
            new StagedFile
            {
                Path = path,
                FileName = name,
                Preset = preset is { Length: > 0 } ? preset : null,
            }
        );

        return path;
    }

    private StemRecut Recut(TrackInstructionStore? settings = null) =>
        new(settings ?? Settings(), WorkingSetWithSource(), Staged());

    [Fact]
    public void It_refuses_when_nothing_of_the_track_is_staged()
    {
        var plan = Recut().PlanFor(Video, StemPlan.Default, Catalog);

        Assert.False(plan.IsPossible);
        Assert.Contains("Sample Staging", plan.Refusal);
    }

    [Fact]
    public void A_source_can_be_added_to_a_track_whose_stems_already_left_for_the_library()
    {
        // Everything is in Mashup Samples, and only a copy of the source is asked for.
        var known = StemInventory.Of(
            [
                new StemOnDisk
                {
                    Path = "M:/Vocal - Full.flac",
                    Root = "M:/",
                    Preset = "Vocal - Full",
                },
                new StemOnDisk
                {
                    Path = "M:/Instrumental - Full.flac",
                    Root = "M:/",
                    Preset = "Instrumental - Full",
                },
            ],
            Catalog
        );

        var wanted = StemPlan.Default with { KeepSource = true };

        var plan = Recut().PlanFor(Video, wanted, Catalog, known);

        Assert.True(plan.IsPossible);
        Assert.Empty(plan.Recipe.Presets);
        Assert.True(plan.Recipe.KeepSource);
        Assert.Empty(plan.Removing);
    }

    [Fact]
    public void A_preset_already_in_the_library_is_never_offered_for_deletion()
    {
        // crateside never deletes from Mashup Samples (ADR 0019).
        var known = StemInventory.Of(
            [
                new StemOnDisk
                {
                    Path = "M:/Vocal - Full.flac",
                    Root = "M:/",
                    Preset = "Vocal - Full",
                },
            ],
            Catalog
        );

        var wanted = new StemPlan { Presets = ["instrumental_full"] };

        var plan = Recut().PlanFor(Video, wanted, Catalog, known);

        Assert.DoesNotContain(plan.Removing, stem => stem.File.Path == "M:/Vocal - Full.flac");
    }

    [Fact]
    public void It_refuses_when_the_separator_could_not_say_what_these_files_are()
    {
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
    public void Dropping_stems_that_were_never_made_records_the_plan_and_makes_nothing()
    {
        // The source sits in Mashup Samples; the stored plan still asks for the usual pair.
        var known = StemInventory.Of(
            [new StemOnDisk { Path = "M:/Rob Dougan - Furious Angels.flac", Root = "M:/" }],
            Catalog
        );
        var settings = Settings();
        var sourceOnly = new StemPlan { Presets = [], KeepSource = true };

        var plan = Recut(settings).PlanFor(Video, sourceOnly, Catalog, known);
        var outcome = Recut(settings).Apply(Video, sourceOnly, Catalog, known);

        Assert.True(plan.IsPossible);
        Assert.False(plan.MakesSomething);
        Assert.True(outcome.Succeeded);
        Assert.True(
            settings.Read().Single(track => track.VideoId == Video).Plan?.AsksTheSameAs(sourceOnly)
        );
    }

    [Fact]
    public void A_track_whose_audio_has_gone_is_re_cut_by_fetching_it_again()
    {
        Stage();

        var recut = new StemRecut(Settings(), new WorkingSet(_working), Staged());

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

        var outcome = Recut(settings).Apply(Video, wanted, Catalog);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, outcome.Removed);
        Assert.False(File.Exists(vocal));
        Assert.True(File.Exists(instrumental));

        Assert.True(File.Exists(guide));

        var stored = settings.Read().Single(track => track.VideoId == Video).Plan;

        Assert.NotNull(stored);
        Assert.True(stored.AsksTheSameAs(wanted));
    }

    [Fact]
    public void An_instrumental_release_that_turns_out_to_have_vocals_loses_its_source()
    {
        // Marked as having no vocals, and the vocals turn out to be there after all.
        var source = Stage();
        var guide = Stage("Drums - htdemucs_ft");

        var outcome = Recut().Apply(Video, StemPlan.Default, Catalog);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, outcome.Removed);
        Assert.False(File.Exists(source));

        Assert.True(File.Exists(guide));
    }

    [Fact]
    public void It_clears_Abletons_analysis_files_along_with_the_audio()
    {
        // A sidecar left behind would be picked up by the new audio under the same name.
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

    [Fact]
    public void A_re_cut_is_recorded_as_a_request_and_leaves_the_job_history_alone()
    {
        Stage();

        var jobs = Jobs();
        var settings = Settings();

        jobs.Record(Video, JobStage.Separation, JobStatus.Succeeded);

        Recut(settings).Apply(Video, StemPlan.Default, Catalog);

        Assert.True(jobs.TryGet(Video, JobStage.Separation, out var separation));
        Assert.Equal(JobStatus.Succeeded, separation.Status);

        var requested = Assert.Single(settings.Read()).CutRequestedAt;

        Assert.NotNull(requested);
        Assert.True(CutRequests.OwesCut(requested, separation));
    }

    [Fact]
    public void Keeping_only_the_source_asks_for_no_GPU_and_stays_unsettled_until_a_handoff()
    {
        Stage("Vocal - Full");
        Stage("Instrumental - Full");
        Stage("Drums - htdemucs_ft");

        var settings = Settings();

        var outcome = Recut(settings)
            .Apply(Video, StemPlan.Default with { KeepSource = true }, Catalog);

        Assert.Equal(0, outcome.Recipe!.Passes);

        var requested = Assert.Single(settings.Read()).CutRequestedAt;

        Assert.False(CutRequests.IsSettled(requested, handoff: null));
    }

    [Theory]
    [InlineData(JobStage.Evidence)]
    [InlineData(JobStage.Verdict)]
    public void It_leaves_the_stages_that_cost_quota_alone(JobStage stage)
    {
        Stage();

        var jobs = Jobs();

        jobs.Record(Video, stage, JobStatus.Succeeded);

        Recut().Apply(Video, StemPlan.Default, Catalog);

        Assert.True(jobs.TryGet(Video, stage, out var job));
        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    [Fact]
    public void A_change_that_only_drops_files_asks_for_nothing()
    {
        Stage("Vocal - Full");
        Stage("Instrumental - Full");

        var settings = Settings();

        var outcome = Recut(settings)
            .Apply(Video, new StemPlan { Presets = ["instrumental_full"] }, Catalog);

        Assert.Equal(1, outcome.Removed);
        Assert.Null(Assert.Single(settings.Read()).CutRequestedAt);
    }

    [Fact]
    public void A_file_it_cannot_delete_stops_it_before_anything_is_asked_for()
    {
        // Nearly always a file Ableton has open.
        Stage("Instrumental - Full");

        var held = Stage("Vocal - Full");
        var settings = Settings();
        var recut = Recut(settings);

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

        Assert.Empty(settings.Read());
    }

    private string StageAdopted(string label)
    {
        var name = $"Rob Dougan - Furious Angels ({label}).flac";
        var path = Path.Combine(_staging, name);

        File.WriteAllText(path, "mix");
        _staged.Add(
            new StagedFile
            {
                Path = path,
                FileName = name,
                AdoptedAs = label,
            }
        );

        return path;
    }

    [Fact]
    public void A_recut_that_drops_the_source_leaves_an_adopted_sample_alone()
    {
        var source = Stage();
        var mix = StageAdopted("Instrumental - Mix");

        var wanted = new StemPlan { Presets = ["vocal_full"], KeepSource = false }.Keeping(
            new AdoptedSample { Label = "Instrumental - Mix" }
        );

        var plan = Recut().PlanFor(Video, wanted, Catalog);

        Assert.True(plan.IsPossible);
        Assert.Contains(plan.Removing, stem => stem.File.Path == source);
        Assert.DoesNotContain(plan.Removing, stem => stem.File.Path == mix);
    }

    [Fact]
    public void A_recut_removes_an_adopted_sample_once_the_plan_forgets_it()
    {
        Stage();
        var mix = StageAdopted("Instrumental - Mix");

        var wanted = new StemPlan { Presets = ["vocal_full"], KeepSource = true };

        var plan = Recut().PlanFor(Video, wanted, Catalog);

        Assert.Contains(plan.Removing, stem => stem.File.Path == mix);
    }
}

/// <summary>
/// How long the GPU will be busy, which is the number the confirmation has to quote.
/// </summary>
public sealed class SeparationEstimateTests
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
        Assert.Equal(1, SeparationRecipe.InstrumentalRelease.Passes);
        Assert.Equal(1, SeparationRecipe.InstrumentalRelease.ExpectedOutputs);
    }
}
