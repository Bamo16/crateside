using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration;

public class TrackSwapTests : IDisposable
{
    private const string Video = "dQw4w9WgXcQ";
    private const string Replacement = "https://www.youtube.com/watch?v=NSLYAT7sjAA";
    private const string Song = "rob dougan - furious angels";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string StagingRoot => Path.Combine(_root, "staging");
    private string LedgerPath => Path.Combine(_root, "runs.jsonl");
    private string WorkingRoot => Path.Combine(_root, "working");

    public TrackSwapTests()
    {
        Directory.CreateDirectory(StagingRoot);
        WriteLedger(Line("2026-09-01T00:00:00+00:00", Song, Video));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_unreadable_url_is_refused()
    {
        Assert.Contains(
            "not a YouTube video ID or URL",
            Swap().PlanFor(Video, "the good one").Refusal
        );
    }

    [Fact]
    public void The_url_it_already_has_is_refused()
    {
        Assert.Contains(
            "already this track's source",
            Swap().PlanFor(Video, $"https://www.youtube.com/watch?v={Video}").Refusal
        );
    }

    [Fact]
    public void A_track_the_ledger_has_never_seen_can_still_be_swapped()
    {
        // It used to be refused for having no verdict to carry across. But a premium shortfall
        // stops a track *before* the verdict, so refusing the unjudged case refused exactly the
        // tracks that most need a better upload (ADR 0016).
        var plan = Swap().PlanFor("neverJudged", Replacement);

        Assert.True(plan.IsPossible);
        Assert.False(plan.CarriesVerdict);
        Assert.Contains("nothing to carry across", plan.Detail);
    }

    [Fact]
    public void A_judged_track_still_says_the_verdict_comes_with_it()
    {
        var plan = Swap().PlanFor(Video, Replacement);

        Assert.True(plan.CarriesVerdict);
        Assert.Contains("key and BPM carry across", plan.Detail);
    }

    [Fact]
    public void The_plan_names_the_staged_files_the_swap_would_remove()
    {
        Stage("Rob Dougan - Furious Angels (Vocal - Full).flac");
        Stage("Rob Dougan - Furious Angels (Instrumental - Full).flac");

        var plan = Swap().PlanFor(Video, Replacement);

        Assert.True(plan.IsPossible);
        Assert.Equal("NSLYAT7sjAA", plan.NewVideoId);
        Assert.Equal(2, plan.Removing.Count);
    }

    [Fact]
    public async Task Applying_records_the_url_clears_the_old_audio_and_reopens_separation()
    {
        Stage("Rob Dougan - Furious Angels (Vocal - Full).flac");
        GiveSource();

        var settings = Settings();
        var jobs = Jobs();
        var workingSet = new WorkingSet(WorkingRoot);

        // Already restated, so nothing shells out to keytag.
        WriteLedger(
            Line("2026-09-01T00:00:00+00:00", Song, Video),
            Line("2026-09-09T00:00:00+00:00", Song, "NSLYAT7sjAA")
        );

        var outcome = await Swap(settings, jobs, workingSet)
            .ApplyAsync(Video, Replacement, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded, outcome.Problem);
        Assert.Equal(1, outcome.Removed);

        var instruction = Assert.Single(settings.Read().Tracks);
        Assert.Equal("https://music.youtube.com/watch?v=NSLYAT7sjAA", instruction.SourceUrl);

        // The retained download is the old upload; separation prefers it over the URL.
        Assert.False(workingSet.TryGetSource(Video, out _));

        foreach (var stage in (JobStage[])[JobStage.Separation, JobStage.Handoff, JobStage.Tagging])
        {
            Assert.True(jobs.TryGet(Video, stage, out var job));
            Assert.Equal(JobStatus.Cancelled, job.Status);
        }
    }

    [Fact]
    public async Task The_verdict_still_resolves_through_both_video_ids_afterwards()
    {
        // The whole point: the key survives the swap, so nothing is judged again.
        WriteLedger(
            Line("2026-09-01T00:00:00+00:00", Song, Video),
            Line("2026-09-09T00:00:00+00:00", Song, "NSLYAT7sjAA")
        );

        await Swap().ApplyAsync(Video, Replacement, TestContext.Current.CancellationToken);

        var ledger = KeyLedger.Read(LedgerPath);

        Assert.True(ledger.TryGetLatestByVideoId(Video, out var viaOld));
        Assert.True(ledger.TryGetLatestByVideoId("NSLYAT7sjAA", out var viaNew));
        Assert.Equal("C minor", viaOld.Key);
        Assert.Equal(viaOld.Key, viaNew.Key);
    }

    [Fact]
    public async Task A_swapped_track_is_separated_from_the_stated_url()
    {
        WriteLedger(
            Line("2026-09-01T00:00:00+00:00", Song, Video),
            Line("2026-09-09T00:00:00+00:00", Song, "NSLYAT7sjAA")
        );

        var settings = Settings();

        await Swap(settings).ApplyAsync(Video, Replacement, TestContext.Current.CancellationToken);

        var item = QueueView
            .Compose(
                [
                    new QueueItem
                    {
                        PlaylistItemId = "PLI1",
                        VideoId = Video,
                        Title = "Rob Dougan - Furious Angels",
                    },
                ],
                new HashSet<string>(),
                new HashSet<string>(),
                new HashSet<string>(),
                settings.Read().Tracks.ToDictionary(t => t.VideoId, StringComparer.Ordinal)
            )
            .Items.Single();

        Assert.Equal(
            "https://music.youtube.com/watch?v=NSLYAT7sjAA",
            WorkOrder.Compose(item, KeyLedger.Read(LedgerPath)).SourceUrl
        );
    }

    [Fact]
    public void A_bare_video_id_is_accepted_and_canonicalised()
    {
        // keytag requires an http(s) URL, so a bare ID has to be turned into one rather than
        // passed on and refused.
        var plan = Swap().PlanFor(Video, "NSLYAT7sjAA");

        Assert.True(plan.IsPossible);
        Assert.Equal("NSLYAT7sjAA", plan.NewVideoId);
        Assert.Equal("https://music.youtube.com/watch?v=NSLYAT7sjAA", plan.Url);
    }

    [Fact]
    public async Task A_reopened_track_is_separated_from_the_stated_url()
    {
        // The bug the first real swap hit: reopened tracks were built with their own list of
        // instruction fields, which did not include the source URL, so separation silently used
        // the playlist entry's own video and re-cut the upload being replaced.
        WriteLedger(
            Line("2026-09-01T00:00:00+00:00", Song, Video),
            Line("2026-09-09T00:00:00+00:00", Song, "NSLYAT7sjAA")
        );

        var settings = Settings();

        await Swap(settings).ApplyAsync(Video, Replacement, TestContext.Current.CancellationToken);

        var reopened = new QueueItem { VideoId = Video, Title = "Rob Dougan - Furious Angels" };

        var item = QueueView
            .Compose(
                [],
                new HashSet<string>(),
                new HashSet<string>(),
                new HashSet<string>(),
                settings.Read().Tracks.ToDictionary(t => t.VideoId, StringComparer.Ordinal),
                [reopened]
            )
            .AwaitingSeparation.Single();

        Assert.Equal(
            "https://music.youtube.com/watch?v=NSLYAT7sjAA",
            WorkOrder.Compose(item, KeyLedger.Read(LedgerPath)).SourceUrl
        );
    }

    [Fact]
    public async Task Swapping_an_unjudged_track_records_the_url_without_restating_anything()
    {
        // Nothing shells out to keytag here, which is what makes this safe: there is no run to
        // restate, and the test's `uv` does not exist.
        var settings = Settings();
        var workingSet = new WorkingSet(WorkingRoot);

        workingSet.SaveAcquisition(
            "neverJudged",
            new Acquisition { PremiumStatus = PremiumStatus.NoPremiumLadder }
        );

        var outcome = await Swap(settings, workingSet: workingSet)
            .ApplyAsync("neverJudged", Replacement, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded, outcome.Problem);

        var instruction = Assert.Single(settings.Read().Tracks);
        Assert.Equal("https://music.youtube.com/watch?v=NSLYAT7sjAA", instruction.SourceUrl);

        // The finding went with the audio it was about, so the replacement is measured afresh.
        Assert.Null(workingSet.AcquisitionFor("neverJudged"));
    }

    private TrackSwap Swap(
        QueueSettingsStore? settings = null,
        JobStore? jobs = null,
        WorkingSet? workingSet = null
    ) =>
        new(
            settings ?? Settings(),
            jobs ?? Jobs(),
            workingSet ?? new WorkingSet(WorkingRoot),
            Staged(),
            new CratesidePaths { Ledger = LedgerPath, SampleStaging = StagingRoot }
        );

    private QueueSettingsStore Settings() =>
        new(Options.Create(new QueueOptions()), Path.Combine(_root, "settings.json"));

    private JobStore Jobs() => new(Path.Combine(_root, "jobs.jsonl"));

    private ReadStagedFiles Staged() =>
        _ =>
            [
                .. Directory
                    .EnumerateFiles(StagingRoot, "*.flac")
                    .Select(path => new StagingStem
                    {
                        Path = path,
                        FileName = Path.GetFileName(path),
                    }),
            ];

    private string Stage(string fileName)
    {
        var path = Path.Combine(StagingRoot, fileName);

        File.WriteAllText(path, "audio");

        return path;
    }

    private void GiveSource()
    {
        var folder = new WorkingSet(WorkingRoot).Prepare(Video);

        File.WriteAllText(Path.Combine(folder, "Rob Dougan - Furious Angels.opus"), "audio");
    }

    private void WriteLedger(params string[] lines) => File.WriteAllLines(LedgerPath, lines);

    private static string Line(string timestamp, string song, string videoId) =>
        JsonSerializer.Serialize(
            new
            {
                timestamp,
                song,
                artist = "Rob Dougan",
                title = "Furious Angels",
                source_url = $"https://www.youtube.com/watch?v={videoId}",
                verdict = new { key = "C minor", confidence = "HIGH" },
                bpm = new { value = 145 },
            }
        );
}
